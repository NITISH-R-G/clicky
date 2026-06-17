import Testing
import AppKit
import Foundation
@testable import leanring_buddy

struct PerformanceTests {
    @Test func testResizePerformance() async throws {
        // Create a large dummy image (e.g. 2560x1600 Retina screen)
        let originalWidth = 2560
        let originalHeight = 1600
        guard let bitmapRep = NSBitmapImageRep(
            bitmapDataPlanes: nil,
            pixelsWide: originalWidth,
            pixelsHigh: originalHeight,
            bitsPerSample: 8,
            samplesPerPixel: 4,
            hasAlpha: true,
            isPlanar: false,
            colorSpaceName: .deviceRGB,
            bytesPerRow: 0,
            bitsPerPixel: 0
        ) else {
            Issue.record("Failed to create dummy image")
            return
        }
        bitmapRep.size = NSSize(width: originalWidth, height: originalHeight)

        NSGraphicsContext.saveGraphicsState()
        let graphicsContext = NSGraphicsContext(bitmapImageRep: bitmapRep)
        NSGraphicsContext.current = graphicsContext
        NSColor.blue.setFill()
        NSRect(x: 0, y: 0, width: originalWidth, height: originalHeight).fill()
        NSGraphicsContext.restoreGraphicsState()

        guard let originalData = bitmapRep.representation(using: .png, properties: [:]) else {
            Issue.record("Failed to create png data")
            return
        }


        let targetWidth = 1280
        let targetHeight = 800

        // --- OLD METHOD ---
        // Warmup
        for _ in 0..<5 {
            let _ = resizeScreenshotForComputerUseOld(originalImageData: originalData, targetWidth: targetWidth, targetHeight: targetHeight)
        }
        let startOld = CFAbsoluteTimeGetCurrent()
        let iterations = 20
        for _ in 0..<iterations {
            let _ = resizeScreenshotForComputerUseOld(originalImageData: originalData, targetWidth: targetWidth, targetHeight: targetHeight)
        }
        let endOld = CFAbsoluteTimeGetCurrent()
        let timeOld = (endOld - startOld) / Double(iterations)
        print("OLD resizing took \(timeOld) seconds per iteration")

        // --- NEW METHOD ---
        // Warmup
        for _ in 0..<5 {
            let _ = resizeScreenshotForComputerUseNew(originalImageData: originalData, targetWidth: targetWidth, targetHeight: targetHeight)
        }
        let startNew = CFAbsoluteTimeGetCurrent()
        for _ in 0..<iterations {
            let _ = resizeScreenshotForComputerUseNew(originalImageData: originalData, targetWidth: targetWidth, targetHeight: targetHeight)
        }
        let endNew = CFAbsoluteTimeGetCurrent()
        let timeNew = (endNew - startNew) / Double(iterations)
        print("NEW resizing took \(timeNew) seconds per iteration")

        print("Improvement: \(timeOld / timeNew)x")
    }

    func resizeScreenshotForComputerUseOld(
        originalImageData: Data,
        targetWidth: Int,
        targetHeight: Int
    ) -> Data? {
        guard let originalImage = NSImage(data: originalImageData) else { return nil }

        guard let bitmapRep = NSBitmapImageRep(
            bitmapDataPlanes: nil,
            pixelsWide: targetWidth,
            pixelsHigh: targetHeight,
            bitsPerSample: 8,
            samplesPerPixel: 4,
            hasAlpha: true,
            isPlanar: false,
            colorSpaceName: .deviceRGB,
            bytesPerRow: 0,
            bitsPerPixel: 0
        ) else {
            return nil
        }

        bitmapRep.size = NSSize(width: targetWidth, height: targetHeight)

        NSGraphicsContext.saveGraphicsState()
        let graphicsContext = NSGraphicsContext(bitmapImageRep: bitmapRep)
        NSGraphicsContext.current = graphicsContext
        graphicsContext?.imageInterpolation = .high
        originalImage.draw(
            in: NSRect(x: 0, y: 0, width: targetWidth, height: targetHeight),
            from: NSRect(origin: .zero, size: originalImage.size),
            operation: .copy,
            fraction: 1.0
        )
        NSGraphicsContext.restoreGraphicsState()

        guard let jpegData = bitmapRep.representation(using: .jpeg, properties: [.compressionFactor: 0.85]) else {
            return nil
        }

        return jpegData
    }

    func resizeScreenshotForComputerUseNew(
        originalImageData: Data,
        targetWidth: Int,
        targetHeight: Int
    ) -> Data? {
        guard let imageSource = CGImageSourceCreateWithData(originalImageData as CFData, nil),
              let cgImage = CGImageSourceCreateImageAtIndex(imageSource, 0, nil) else {
            return nil
        }

        let colorSpace = CGColorSpaceCreateDeviceRGB()
        let bitmapInfo = CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue

        guard let context = CGContext(
            data: nil,
            width: targetWidth,
            height: targetHeight,
            bitsPerComponent: 8,
            bytesPerRow: targetWidth * 4,
            space: colorSpace,
            bitmapInfo: bitmapInfo
        ) else {
            return nil
        }

        context.interpolationQuality = .high
        context.draw(cgImage, in: CGRect(x: 0, y: 0, width: targetWidth, height: targetHeight))

        guard let resizedCGImage = context.makeImage() else {
            return nil
        }

        let bitmapRep = NSBitmapImageRep(cgImage: resizedCGImage)
        guard let jpegData = bitmapRep.representation(using: .jpeg, properties: [.compressionFactor: 0.85]) else {
            return nil
        }

        return jpegData
    }
}
