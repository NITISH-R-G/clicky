//
//  ElementLocationDetectorTests.swift
//  leanring-buddyTests
//
//  Created by Jules.
//

import Testing
import Foundation
@testable import leanring_buddy

struct ElementLocationDetectorTests {
    let detector = ElementLocationDetector(apiKey: "test-key", model: "test-model")

    @Test func successfulCoordinateParsing() throws {
        let jsonString = """
        {
            "content": [
                {
                    "type": "text",
                    "text": "I will click the button."
                },
                {
                    "type": "tool_use",
                    "id": "toolu_123",
                    "name": "computer",
                    "input": {
                        "action": "left_click",
                        "coordinate": [100.5, 200.25]
                    }
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!
        let point = detector.parseCoordinateFromResponse(data: data)

        #expect(point != nil)
        #expect(point?.x == 100.5)
        #expect(point?.y == 200.25)
    }

    @Test func missingToolUseBlock() throws {
        let jsonString = """
        {
            "content": [
                {
                    "type": "text",
                    "text": "no specific element"
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!
        let point = detector.parseCoordinateFromResponse(data: data)

        #expect(point == nil)
    }

    @Test func invalidJsonData() throws {
        let invalidJsonString = "{ invalid_json: "
        let data = invalidJsonString.data(using: .utf8)!
        let point = detector.parseCoordinateFromResponse(data: data)

        #expect(point == nil)
    }

    @Test func missingContentArray() throws {
        let jsonString = """
        {
            "other_field": "value"
        }
        """
        let data = jsonString.data(using: .utf8)!
        let point = detector.parseCoordinateFromResponse(data: data)

        #expect(point == nil)
    }

    @Test func invalidCoordinateFormat() throws {
        // Coordinate array only has 1 number instead of 2
        let jsonString = """
        {
            "content": [
                {
                    "type": "tool_use",
                    "input": {
                        "coordinate": [100.5]
                    }
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!
        let point = detector.parseCoordinateFromResponse(data: data)

        #expect(point == nil)
    }
}
