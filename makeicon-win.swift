// Generates Glass.ico for the Windows build -- the same two panes as makeicon.swift, drawn
// natively at every size Windows asks for, rather than downscaled from one master.
//
//   swiftc -O makeicon-win.swift -o .makeicon-win && ./.makeicon-win Glass.ico
//
// Every entry is an uncompressed 32bpp BMP, not PNG. PNG entries are legal since Vista, but
// not every loader takes them, and the tray icon goes through System.Drawing.Icon.

import AppKit

func rounded(_ r: CGRect, _ radius: CGFloat) -> CGPath {
    CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil)
}

func rgb(_ hex: UInt32, _ a: CGFloat = 1) -> CGColor {
    CGColor(red: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255, alpha: a)
}

/// Premultiplied RGBA, top row first.
func drawIcon(size s: CGFloat) -> [UInt8] {
    let px = Int(s)
    var data = [UInt8](repeating: 0, count: px * px * 4)
    data.withUnsafeMutableBytes { buf in
        let ctx = CGContext(data: buf.baseAddress, width: px, height: px, bitsPerComponent: 8,
                            bytesPerRow: px * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.setShouldAntialias(true)
        ctx.interpolationQuality = .high

        // Windows icons fill the canvas; the macOS squircle inset would leave them small
        // beside every other icon in the taskbar.
        let inset = s <= 24 ? 0 : s * 0.04
        let plate = CGRect(x: inset, y: inset, width: s - inset * 2, height: s - inset * 2)
        let platePath = rounded(plate, plate.width * 0.18)

        ctx.saveGState()
        ctx.addPath(platePath)
        ctx.clip()
        let bg = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB)!,
                            colors: [rgb(0x3C5A72), rgb(0x16222C)] as CFArray, locations: [0, 1])!
        ctx.drawLinearGradient(bg, start: CGPoint(x: 0, y: plate.maxY),
                               end: CGPoint(x: 0, y: plate.minY), options: [])
        ctx.restoreGState()

        // Two panes, side by side: [] []. Slightly larger than the macOS drawing, since there
        // is no inset plate around them eating the space.
        let paneW = s * 0.26
        let paneH = s * 0.44
        // At tray sizes a proportional gap is under a pixel and the panes smear together.
        let gap = max(s * 0.08, s <= 24 ? 2 : 0)
        let totalW = paneW * 2 + gap
        let originY = (s - paneH) / 2
        let radius = max(1, s * 0.04)
        let stroke = max(1, s * 0.03)

        for i in 0..<2 {
            let x = (s - totalW) / 2 + CGFloat(i) * (paneW + gap)
            let pane = CGRect(x: x, y: originY, width: paneW, height: paneH)
            let path = rounded(pane, radius)

            ctx.saveGState()
            ctx.addPath(path)
            ctx.clip()
            let glass = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB)!,
                                   colors: [rgb(0xCFEAFF, 0.34), rgb(0x8FC4E8, 0.10)] as CFArray,
                                   locations: [0, 1])!
            ctx.drawLinearGradient(glass, start: CGPoint(x: 0, y: pane.maxY),
                                   end: CGPoint(x: 0, y: pane.minY), options: [])
            if s >= 64 {
                ctx.setFillColor(rgb(0xFFFFFF, 0.18))
                let sheen = CGMutablePath()
                sheen.move(to: CGPoint(x: pane.minX + paneW * 0.02, y: pane.minY))
                sheen.addLine(to: CGPoint(x: pane.minX + paneW * 0.40, y: pane.minY))
                sheen.addLine(to: CGPoint(x: pane.minX + paneW * 0.86, y: pane.maxY))
                sheen.addLine(to: CGPoint(x: pane.minX + paneW * 0.48, y: pane.maxY))
                sheen.closeSubpath()
                ctx.addPath(sheen)
                ctx.fillPath()
            }
            ctx.restoreGState()

            ctx.setStrokeColor(rgb(0xEAF6FF, 0.92))
            ctx.setLineWidth(stroke)
            ctx.addPath(rounded(pane.insetBy(dx: stroke / 2, dy: stroke / 2), radius))
            ctx.strokePath()
        }
    }
    return data
}

func le16(_ v: Int) -> [UInt8] { [UInt8(v & 0xFF), UInt8((v >> 8) & 0xFF)] }
func le32(_ v: Int) -> [UInt8] { (0..<4).map { UInt8((v >> (8 * $0)) & 0xFF) } }

/// One ICO image: BITMAPINFOHEADER, bottom-up straight-alpha BGRA, then the 1bpp AND mask.
func bmpEntry(size px: Int) -> [UInt8] {
    let rgba = drawIcon(size: CGFloat(px))
    var out: [UInt8] = []
    let maskStride = ((px + 31) / 32) * 4
    out += le32(40) + le32(px) + le32(px * 2) + le16(1) + le16(32) + le32(0)
    out += le32(px * px * 4 + maskStride * px) + le32(0) + le32(0) + le32(0) + le32(0)
    for row in (0..<px).reversed() {                      // BMP rows run bottom-up
        for col in 0..<px {
            let i = (row * px + col) * 4
            let a = Int(rgba[i + 3])
            // CoreGraphics hands back premultiplied colour; icons want it straight.
            func un(_ c: UInt8) -> UInt8 { a == 0 ? 0 : UInt8(min(255, (Int(c) * 255 + a / 2) / a)) }
            out += [un(rgba[i + 2]), un(rgba[i + 1]), un(rgba[i]), UInt8(a)]
        }
    }
    for row in (0..<px).reversed() {
        var bits = [UInt8](repeating: 0, count: maskStride)
        for col in 0..<px where rgba[(row * px + col) * 4 + 3] == 0 {
            bits[col / 8] |= UInt8(0x80 >> (col % 8))     // 1 = transparent, for old loaders
        }
        out += bits
    }
    return out
}

let sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
let images = sizes.map { bmpEntry(size: $0) }
var ico: [UInt8] = le16(0) + le16(1) + le16(sizes.count)
var offset = 6 + 16 * sizes.count
for (s, img) in zip(sizes, images) {
    ico += [UInt8(s >= 256 ? 0 : s), UInt8(s >= 256 ? 0 : s), 0, 0] + le16(1) + le16(32)
    ico += le32(img.count) + le32(offset)
    offset += img.count
}
for img in images { ico += img }

let path = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "Glass.ico"
FileManager.default.createFile(atPath: path, contents: Data(ico))
print("wrote \(path) (\(sizes.map(String.init).joined(separator: ", ")))")
