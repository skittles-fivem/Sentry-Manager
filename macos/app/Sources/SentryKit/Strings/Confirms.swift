import Foundation

/// A confirm dialog's words: the web's title, body (as plain paragraphs)
/// and button. `danger` makes the button destructive.
public struct ConfirmText: Sendable, Equatable {
    public var title: String
    public var paragraphs: [String]
    public var ok: String
    public var danger: Bool

    public init(title: String, paragraphs: [String], ok: String = "Confirm", danger: Bool = false) {
        self.title = title; self.paragraphs = paragraphs; self.ok = ok; self.danger = danger
    }
}

public enum Confirms {
    /// "Sunday, Jun 1, 2025" (the web's dayTitle in English)
    public static func dayTitle(_ day: String) -> String {
        let p = day.split(separator: "-").compactMap { Int($0) }
        guard p.count == 3 else { return day }
        var c = DateComponents()
        c.year = p[0]; c.month = p[1]; c.day = p[2]; c.hour = 12
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        guard let d = cal.date(from: c) else { return day }
        let weekday = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"][cal.component(.weekday, from: d) - 1]
        let month = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"][p[1] - 1]
        return "\(weekday), \(month) \(p[2]), \(p[0])"
    }

    public static func trashRecordings(count: Int, bytes: Double, anyProtected: Bool) -> ConfirmText {
        var ps = ["\(Format.bytes(bytes)) moves to the Recycle Bin on the same share. Nothing is deleted: restore it any time, or delete it forever from the Recycle Bin."]
        if anyProtected { ps.append("Protected recordings are skipped.") }
        return ConfirmText(title: "Move \(Format.plural(Double(count), "recording")) to the Recycle Bin?", paragraphs: ps, ok: "Move to trash")
    }

    public static func trashDays(day: String, through: Bool, category: String, preview: TrashDaysAnswer) -> ConfirmText {
        let what = through ? "\(dayTitle(day)) and every older day" : dayTitle(day)
        let scope = category == "all" ? "" : " (\(category) only, as filtered)"
        let days = preview.days ?? 0
        let over = through && days > 1 ? " over \(Format.plural(days, "day"))" : ""
        let prot = preview.protected ?? 0
        let stays = prot > 0 ? " \(Format.plural(prot, "protected recording")) stay\(prot == 1 ? "s" : "")." : ""
        return ConfirmText(
            title: through ? "Delete through this date?" : "Delete this day?",
            paragraphs: [
                "\(Format.plural(preview.recordings, "recording")) from \(what)\(scope), \(Format.bytes(preview.bytes))\(over), move to the Recycle Bin.",
                "Nothing is deleted for good until you empty the Recycle Bin; you can restore it until then.\(stays)",
            ],
            ok: through ? "Delete through this date" : "Delete this day", danger: true)
    }

    /// nil means a toast instead: nothing to move
    public static func trashDaysNothing(_ preview: TrashDaysAnswer) -> String? {
        guard (preview.recordings ?? 0) == 0 else { return nil }
        return (preview.protected ?? 0) > 0 ? "Everything there is protected: nothing to move." : "Nothing to move."
    }

    public static func runPolicy(_ preview: PolicyPreview) -> ConfirmText {
        ConfirmText(title: "Run the policy now?",
                    paragraphs: ["\(Format.plural(preview.recordings, "recording")) (\(Format.bytes(preview.bytes))) will move to the Recycle Bin. You can restore them from there."],
                    ok: "Run now")
    }

    public static func clearJobs(count: Double, vehicleName: String?, manyVehicles: Bool) -> ConfirmText {
        let of = vehicleName != nil && manyVehicles ? " of \(vehicleName!)" : ""
        return ConfirmText(title: "Clear finished jobs?", paragraphs: [
            "\(Format.plural(count, "finished job")) (done, failed, cancelled or interrupted)\(of) leave\(count == 1 ? "s" : "") the list. Running, waiting and paused jobs stay.",
            "Only the list is cleared: footage, converted files and what the Encoder needs to resume a conversion are not touched. New jobs show up as before.",
        ], ok: "Clear jobs")
    }

    public static func deleteForever(label: String, bytes: Double) -> ConfirmText {
        ConfirmText(title: "Delete forever?", paragraphs: ["\(label) (\(Format.bytes(bytes))) will be permanently deleted from the share. This cannot be undone."],
                    ok: "Delete forever", danger: true)
    }

    public static func emptyBin(items: Int, bytes: Double, vehicleName: String?) -> ConfirmText {
        ConfirmText(title: "Empty the Recycle Bin?",
                    paragraphs: ["\(Format.plural(Double(items), "item")) (\(Format.bytes(bytes))) of \(vehicleName ?? "all vehicles") will be permanently deleted. This cannot be undone."],
                    ok: "Empty Recycle Bin", danger: true)
    }

    public static let clearLogs = ConfirmText(title: "Clear the logs?", paragraphs: [
        "The logs start empty. Nothing is deleted: the current lines move to logs/archive/ in the data folder, and you can download them under Cleared logs.",
    ], ok: "Clear logs")

    public static func removeNode(_ name: String) -> ConfirmText {
        ConfirmText(title: "Remove this node?", paragraphs: ["\(name) gets no more clips from this server. To use it again, pair it again with a new code."], ok: "Remove")
    }

    public static func revokeMain(_ name: String) -> ConfirmText {
        ConfirmText(title: "Revoke this main server?", paragraphs: ["\(name) can no longer send clips here. Clips it is converting now stop."], ok: "Revoke", danger: true)
    }

    public static func deletePack(_ name: String, isActive: Bool) -> ConfirmText {
        ConfirmText(title: "Delete this interface?", paragraphs: ["\(name) is deleted from the server.\(isActive ? " Everyone gets the original interface." : "")"], ok: "Delete", danger: true)
    }

    public static func usePackNow(_ name: String) -> ConfirmText {
        ConfirmText(title: "Use it now?", paragraphs: ["\(name) is installed. Use it for everyone now? You can switch back to Original here at any time."], ok: "Use it")
    }

    public static let newCertificate = ConfirmText(title: "Make a new certificate?", paragraphs: [
        "The certificate will cover the new names. Every phone, tablet and computer that continued past the browser's warning before will see it once more; devices with the certificate authority installed won't.",
    ], ok: "Save names")

    public static let removeVehicle = ConfirmText(title: "Remove this vehicle?",
                                                  paragraphs: ["Sentry Six Manager forgets the vehicle and unmounts its share. No footage is deleted."], ok: "Remove", danger: true)

    public static func replaceOriginals(_ text: String) -> ConfirmText {
        ConfirmText(title: "Replace originals?", paragraphs: [text], ok: "Replace originals", danger: true)
    }
}
