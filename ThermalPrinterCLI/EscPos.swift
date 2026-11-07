import Foundation

/// ESC/POS command byte sequences (taken from the BitBank2 repo)
struct EscPos {
    static let initialize = Data([0x1B, 0x40])            // ESC @
    static let lineFeed   = Data([0x0A])                  // LF

    static func bold(_ on: Bool) -> Data {
        Data([0x1B, 0x45, on ? 0x01 : 0x00])             // ESC E n
    }

    static func underline(_ on: Bool) -> Data {
        Data([0x1B, 0x2D, on ? 0x01 : 0x00])             // ESC - n
    }

    static func doubleHeightWidth(_ on: Bool) -> Data {
        Data([0x1B, 0x21, on ? 0x30 : 0x00])             // ESC ! n (0x30 = double‑h/w)
    }
}

/// Convert a tiny markdown‑like string into a raw ESC/POS byte stream.
func convert(markdown: String) -> Data {
    var out = Data()
    out.append(EscPos.initialize)

    let lines = markdown.components(separatedBy: .newlines)

    for var rawLine in lines {
        var linePrefix = Data()
        var lineSuffix = Data()

        // ---- Heading -------------------------------------------------
        if rawLine.hasPrefix("# ") {
            linePrefix.append(EscPos.doubleHeightWidth(true))
            rawLine.removeFirst(2)               // drop "# "
            lineSuffix.append(EscPos.doubleHeightWidth(false))
        } else {
            linePrefix.append(EscPos.doubleHeightWidth(false))
        }

        // ---- Bold (**text**) -----------------------------------------
        rawLine = replace(regex: #"\*\*(.+?)\*\*"#, in: rawLine) { match in
            let inner = match[1] ?? ""
            var d = Data()
            d.append(EscPos.bold(true))
            d.append(inner.data(using: .utf8) ?? Data())
            d.append(EscPos.bold(false))
            return d
        }

        // ---- Italic (*text*) → underline -------------------------------
        rawLine = replace(regex: #"\*(.+?)\*"#, in: rawLine) { match in
            let inner = match[1] ?? ""
            var d = Data()
            d.append(EscPos.underline(true))
            d.append(inner.data(using: .utf8) ?? Data())
            d.append(EscPos.underline(false))
            return d
        }

        // ---- Assemble line --------------------------------------------
        out.append(linePrefix)
        out.append(rawLine.data(using: .utf8) ?? Data())
        out.append(lineSuffix)
        out.append(EscPos.lineFeed)
    }

    // final feed (printer‑specific; one extra LF works for most models)
    out.append(EscPos.lineFeed)
    return out
}

/// Helper: run a regex, then splice the result of the closure into a `Data` buffer.
private func replace(regex pattern: String,
                     in text: String,
                     with builder: ([String?]) -> Data) -> String {
    guard let re = try? NSRegularExpression(pattern: pattern, options: []) else {
        return text
    }

    var result = Data()
    var lastPos = text.startIndex

    let nsText = text as NSString
    let matches = re.matches(in: text, options: [], range: NSRange(location: 0, length: nsText.length))

    for match in matches {
        // plain segment before this match
        let rangeBefore = lastPos..<text.index(text.startIndex, offsetBy: match.range.location)
        result.append(text[rangeBefore].data(using: .utf8) ?? Data())

        // capture groups
        var groups: [String?] = []
        for i in 0..<match.numberOfRanges {
            let r = match.range(at: i)
            if r.location != NSNotFound {
                groups.append(nsText.substring(with: r))
            } else {
                groups.append(nil)
            }
        }

        // replacement bytes
        result.append(builder(groups))

        // move cursor
        let after = match.range.location + match.range.length
        lastPos = text.index(text.startIndex, offsetBy: after)
    }

    // trailing plain text
    result.append(text[lastPos...].data(using: .utf8) ?? Data())
    return String(data: result, encoding: .utf8) ?? text
}
