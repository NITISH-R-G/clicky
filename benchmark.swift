import Foundation

// Dummy structures to mimic AppKit/ScreenCaptureKit
struct CGPoint { var x, y: Double }
struct CGSize { var width, height: Double }
struct CGRect {
    var origin: CGPoint
    var size: CGSize
    var width: Double { size.width }
    var height: Double { size.height }
    func contains(_ point: CGPoint) -> Bool {
        return point.x >= origin.x && point.x <= origin.x + size.width &&
               point.y >= origin.y && point.y <= origin.y + size.height
    }
}

typealias CGDirectDisplayID = UInt32

class SCDisplay {
    let displayID: CGDirectDisplayID
    let frame: CGRect
    let width: Int
    let height: Int
    init(displayID: CGDirectDisplayID, frame: CGRect, width: Int, height: Int) {
        self.displayID = displayID
        self.frame = frame
        self.width = width
        self.height = height
    }
}

class NSScreen {
    let frame: CGRect
    init(frame: CGRect) { self.frame = frame }
}

let numDisplays = 10
var displays: [SCDisplay] = []
var nsScreenByDisplayID: [CGDirectDisplayID: NSScreen] = [:]

for i in 0..<numDisplays {
    let id = CGDirectDisplayID(i)
    let frame = CGRect(origin: CGPoint(x: Double(i)*100, y: Double(i)*100), size: CGSize(width: 100, height: 100))
    displays.append(SCDisplay(displayID: id, frame: frame, width: 100, height: 100))
    if i % 2 == 0 {
        nsScreenByDisplayID[id] = NSScreen(frame: frame)
    }
}

let mouseLocation = CGPoint(x: 150, y: 150) // within display 1, which has no NSScreen fallback, so uses display.frame

func benchmarkBaseline() {
    let start = CFAbsoluteTimeGetCurrent()
    for _ in 0..<100000 {
        let sortedDisplays = displays.sorted { displayA, displayB in
            let frameA = nsScreenByDisplayID[displayA.displayID]?.frame ?? displayA.frame
            let frameB = nsScreenByDisplayID[displayB.displayID]?.frame ?? displayB.frame
            let aContainsCursor = frameA.contains(mouseLocation)
            let bContainsCursor = frameB.contains(mouseLocation)
            if aContainsCursor != bContainsCursor { return aContainsCursor }
            return false
        }

        var isCursorFoundCount = 0
        for (displayIndex, display) in sortedDisplays.enumerated() {
            let displayFrame = nsScreenByDisplayID[display.displayID]?.frame
                ?? CGRect(origin: CGPoint(x: display.frame.origin.x, y: display.frame.origin.y),
                          size: CGSize(width: Double(display.width), height: Double(display.height)))
            let isCursorScreen = displayFrame.contains(mouseLocation)
            if isCursorScreen { isCursorFoundCount += 1 }
        }
    }
    let end = CFAbsoluteTimeGetCurrent()
    print("Baseline: \(end - start) seconds")
}

func benchmarkOptimized() {
    let start = CFAbsoluteTimeGetCurrent()
    for _ in 0..<100000 {
        // Cache the lookup result for each display
        let displayInfos = displays.map { display -> (display: SCDisplay, frame: CGRect, isCursorScreen: Bool) in
            let frame = nsScreenByDisplayID[display.displayID]?.frame
                ?? CGRect(origin: CGPoint(x: display.frame.origin.x, y: display.frame.origin.y),
                          size: CGSize(width: Double(display.width), height: Double(display.height)))
            let isCursorScreen = frame.contains(mouseLocation)
            return (display, frame, isCursorScreen)
        }

        // Sort displays so the cursor screen is always first
        let sortedDisplays = displayInfos.sorted { a, b in
            if a.isCursorScreen != b.isCursorScreen { return a.isCursorScreen }
            return false
        }

        var isCursorFoundCount = 0
        for (displayIndex, displayInfo) in sortedDisplays.enumerated() {
            let displayFrame = displayInfo.frame
            let isCursorScreen = displayInfo.isCursorScreen
            if isCursorScreen { isCursorFoundCount += 1 }
        }
    }
    let end = CFAbsoluteTimeGetCurrent()
    print("Optimized: \(end - start) seconds")
}

benchmarkBaseline()
benchmarkOptimized()
