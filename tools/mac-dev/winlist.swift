import CoreGraphics
import Foundation
let opts: CGWindowListOption = [.optionOnScreenOnly, .excludeDesktopElements]
if let list = CGWindowListCopyWindowInfo(opts, kCGNullWindowID) as? [[String: Any]] {
    for w in list {
        let owner = w[kCGWindowOwnerName as String] as? String ?? ""
        let name = w[kCGWindowName as String] as? String ?? ""
        let num = w[kCGWindowNumber as String] as? Int ?? 0
        let b = w[kCGWindowBounds as String] as? [String: Any] ?? [:]
        print(num, "|", owner, "|", name, "|", b["Width"] ?? "", "x", b["Height"] ?? "")
    }
}
