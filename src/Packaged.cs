// Is this copy of Glass running from an MSIX package (the Microsoft Store build), or is it the
// plain Glass.exe out of the zip?
//
// It matters in more places than it looks. A Store build lives in C:\Program Files\WindowsApps,
// signed by Microsoft, and cannot replace itself with something downloaded from GitHub -- so the
// update check has to be off, not merely quiet. Store policy 10.1.5 also only allows acquiring
// other products through the Store, and a build that offers a GitHub download is asking to fail
// certification.
//
// The test is the documented one: GetCurrentPackageFullName returns APPMODEL_ERROR_NO_PACKAGE
// when there is no package identity. It is asked once -- the answer cannot change while we run.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Glass
{
    public static class Packaged
    {
        const int APPMODEL_ERROR_NO_PACKAGE = 15700;
        const int ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);

        static bool? packaged;
        static string fullName;

        /// True when Glass is running from an MSIX package.
        public static bool Is
        {
            get
            {
                if (packaged == null) Ask();
                return packaged.Value;
            }
        }

        /// The package full name, or null when unpackaged. Goes in the report, so a log from a
        /// Store copy is never mistaken for one from the zip.
        public static string FullName
        {
            get
            {
                if (packaged == null) Ask();
                return fullName;
            }
        }

        static void Ask()
        {
            try
            {
                int length = 0;
                int rc = GetCurrentPackageFullName(ref length, null);
                if (rc == APPMODEL_ERROR_NO_PACKAGE) { packaged = false; return; }

                // Anything other than "tell me the length" means we cannot say; treat an
                // unreadable answer as unpackaged, which is the safe way round -- the zip
                // build's behaviour is the one that works everywhere.
                if (rc != ERROR_INSUFFICIENT_BUFFER) { packaged = false; return; }

                var buffer = new StringBuilder(length);
                if (GetCurrentPackageFullName(ref length, buffer) != 0) { packaged = false; return; }
                fullName = buffer.ToString();
                packaged = true;
            }
            catch (Exception e)
            {
                // Missing on nothing we support, but a DllNotFoundException here would
                // otherwise take the whole startup down.
                Log.Write("package check failed, assuming unpackaged: " + e.Message);
                packaged = false;
            }
        }
    }
}
