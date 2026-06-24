import XCTest
import ScreenCaptureKit
@testable import leanring_buddy

class ConfigurationPerformanceTests: XCTestCase {
    func testInstantiatingInLoop() {
        measure {
            for _ in 0..<10000 {
                let config = SCStreamConfiguration()
                config.width = 1280
                config.height = 720
            }
        }
    }

    func testReusingInstance() {
        let config = SCStreamConfiguration()
        measure {
            for _ in 0..<10000 {
                config.width = 1280
                config.height = 720
            }
        }
    }
}
