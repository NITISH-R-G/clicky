//
//  ElementLocationDetectorTests.swift
//  leanring-buddyTests
//

import Testing
import Foundation
@testable import leanring_buddy

struct ElementLocationDetectorTests {

    // Helper to initialize detector without making actual network requests
    // Using a dummy API key since we only test JSON parsing
    let detector = ElementLocationDetector(apiKey: "dummy_key")

    @Test func validToolUseBlockReturnsCoordinate() throws {
        let jsonString = """
        {
            "content": [
                {
                    "type": "text",
                    "text": "Here is the element you asked for."
                },
                {
                    "type": "tool_use",
                    "id": "toolu_01A09q90qw90lq9178bf489b",
                    "name": "computer",
                    "input": {
                        "action": "left_click",
                        "coordinate": [123.4, 567.8]
                    }
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!

        let result = detector.parseCoordinateFromResponse(data: data)

        #expect(result != nil)
        #expect(result?.x == 123.4)
        #expect(result?.y == 567.8)
    }

    @Test func textOnlyResponseReturnsNil() throws {
        let jsonString = """
        {
            "content": [
                {
                    "type": "text",
                    "text": "There is no specific element to click on."
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!

        let result = detector.parseCoordinateFromResponse(data: data)

        #expect(result == nil)
    }

    @Test func invalidJSONReturnsNil() throws {
        let invalidJSONString = "{ invalid json: true "
        let data = invalidJSONString.data(using: .utf8)!

        let result = detector.parseCoordinateFromResponse(data: data)

        #expect(result == nil)
    }

    @Test func missingContentBlockReturnsNil() throws {
        let jsonString = """
        {
            "model": "claude-sonnet",
            "role": "assistant"
        }
        """
        let data = jsonString.data(using: .utf8)!

        let result = detector.parseCoordinateFromResponse(data: data)

        #expect(result == nil)
    }

    @Test func missingCoordinateArrayReturnsNil() throws {
        let jsonString = """
        {
            "content": [
                {
                    "type": "tool_use",
                    "id": "toolu_01A09q90qw90lq9178bf489b",
                    "name": "computer",
                    "input": {
                        "action": "left_click"
                    }
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!

        let result = detector.parseCoordinateFromResponse(data: data)

        #expect(result == nil)
    }

    @Test func wrongCoordinateCountReturnsNil() throws {
        let jsonString = """
        {
            "content": [
                {
                    "type": "tool_use",
                    "id": "toolu_01A09q90qw90lq9178bf489b",
                    "name": "computer",
                    "input": {
                        "action": "left_click",
                        "coordinate": [123.4]
                    }
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!

        let result = detector.parseCoordinateFromResponse(data: data)

        #expect(result == nil)
    }

    @Test func unexpectedToolNameIsIgnored() throws {
        // Technically, the function doesn't check the tool name, it only checks type="tool_use".
        // But if it were to ignore other tools, we'd test that. Given the implementation, it accepts any tool_use with a coordinate.
        // Let's test a tool use with coordinates is accepted regardless of name.
        let jsonString = """
        {
            "content": [
                {
                    "type": "tool_use",
                    "id": "toolu_01A09q90qw90lq9178bf489b",
                    "name": "other_tool",
                    "input": {
                        "coordinate": [10.0, 20.0]
                    }
                }
            ]
        }
        """
        let data = jsonString.data(using: .utf8)!

        let result = detector.parseCoordinateFromResponse(data: data)

        #expect(result != nil)
        #expect(result?.x == 10.0)
        #expect(result?.y == 20.0)
    }
}
