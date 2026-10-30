import Foundation

/// Every endpoint the app uses, by name. Paths and fields as server/index.js.
public enum API {
    private static func p(_ s: String) -> String { PathEncoding.component(s) }

    // MARK: sign-in
    public static func me() -> Endpoint<AuthMe> { .init(.get, "/api/auth/me") }
    public static func info() -> Endpoint<ServerInfo> { .init(.get, "/api/info") }
    public static func login(username: String, password: String) -> Endpoint<OKAnswer> {
        .init(.post, "/api/auth/login", json: LoginBody(username: username, password: password))
    }
    public static func setup(username: String, password: String, code: String) -> Endpoint<OKAnswer> {
        .init(.post, "/api/auth/setup", json: SetupBody(username: username, password: password, code: code))
    }
    public static func logout() -> Endpoint<OKAnswer> { .init(.post, "/api/auth/logout", json: [String: String]()) }
    public static func confirm(password: String) -> Endpoint<OKAnswer> { .init(.post, "/api/auth/confirm", json: ConfirmBody(password: password)) }
    public static func changePassword(current: String, next: String) -> Endpoint<OKAnswer> {
        .init(.post, "/api/auth/password", json: PasswordBody(current: current, next: next))
    }
    public static func localSignIn(token: String) -> Endpoint<LocalSignInAnswer> {
        .init(.post, "/api/auth/local", json: LocalSignInBody(token: token))
    }

    // MARK: server
    public static func system() -> Endpoint<SystemAnswer> { .init(.get, "/api/system") }
    public static func volumes() -> Endpoint<[Volume]> { .init(.get, "/api/system/volumes") }
    public static func security() -> Endpoint<SecurityAnswer> { .init(.get, "/api/security") }
    public static func saveHostnames(_ names: [String]) -> Endpoint<HostnamesAnswer> {
        .init(.put, "/api/security/hostnames", json: HostnamesBody(hostnames: names))
    }
    public static func logs(limit: Int = 300, level: String? = nil) -> Endpoint<LogsAnswer> {
        var q = [("limit", String(limit))]
        if let level { q.append(("level", level)) }
        return .init(.get, "/api/logs", query: q, timeout: .history)
    }
    public static func clearLogs() -> Endpoint<LogsClearAnswer> { .init(.post, "/api/logs/clear", json: [String: String]()) }
    public static func logArchives() -> Endpoint<LogArchivesAnswer> { .init(.get, "/api/logs/archive") }
    public static func logArchivePath(_ name: String) -> String { "/api/logs/archive/\(p(name))" }
    public static let logDownloadPath = "/api/logs/download"
    public static func stats() -> Endpoint<Stats> { .init(.get, "/api/stats", timeout: .polled) }
    public static func statsHistory(range: String) -> Endpoint<StatsHistory> {
        .init(.get, "/api/stats/history", query: [("range", range)], timeout: .history)
    }
    public static func statsCSVPath(range: String) -> String { "/api/stats/export.csv?range=\(p(range))" }
    public static func updates() -> Endpoint<UpdatesStatus> { .init(.get, "/api/updates") }
    public static func checkUpdates() -> Endpoint<UpdatesStatus> { .init(.post, "/api/updates/check", json: [String: String]()) }

    // MARK: settings
    public static func settings() -> Endpoint<SettingsAnswer> { .init(.get, "/api/settings") }
    public static func setAppearance(_ a: Appearance) -> Endpoint<Appearance> { .init(.put, "/api/settings/appearance", json: a) }
    public static func setPower(_ s: PowerSettings) -> Endpoint<PowerSettings> { .init(.put, "/api/settings/power", json: s) }
    public static func setLocalSignIn(enabled: Bool, user: String?) -> Endpoint<OKAnswer> {
        .init(.put, "/api/settings/local-sign-in", json: LocalSignInSettingBody(enabled: enabled, user: user))
    }

    // MARK: interfaces
    public static func uiPacks() -> Endpoint<UIPacksAnswer> { .init(.get, "/api/ui-packs") }
    public static func setActivePack(_ id: String?) -> Endpoint<UIPackActiveAnswer> { .init(.put, "/api/ui-packs/active", json: UIPackActiveBody(id: id)) }
    public static func deletePack(_ id: String) -> Endpoint<UIPackActiveAnswer> { .init(.delete, "/api/ui-packs/\(p(id))") }
    public static func uploadPack(zip: Data, fileName: String) -> Endpoint<UIPackUploadAnswer> {
        .init(.post, "/api/ui-packs/upload", body: zip, headers: ["X-File-Name": PathEncoding.component(fileName), "Content-Type": "application/zip"])
    }
    public static let originalPackZipPath = "/api/ui-packs/original.zip"

    // MARK: vehicles
    public static func vehicles() -> Endpoint<VehiclesAnswer> { .init(.get, "/api/vehicles") }
    public static func addVehicle(_ body: VehicleBody) -> Endpoint<Vehicle> { .init(.post, "/api/vehicles", json: body) }
    public static func updateVehicle(_ id: String, _ body: VehicleBody) -> Endpoint<Vehicle> { .init(.put, "/api/vehicles/\(p(id))", json: body) }
    public static func deleteVehicle(_ id: String) -> Endpoint<OKAnswer> { .init(.delete, "/api/vehicles/\(p(id))") }
    public static func reconnect(_ id: String) -> Endpoint<ReconnectAnswer> { .init(.post, "/api/vehicles/\(p(id))/reconnect", json: [String: String]()) }
    public static func scan(_ id: String) -> Endpoint<ScanAnswer> { .init(.post, "/api/vehicles/\(p(id))/scan", json: [String: String]()) }
    public static func testSMB(_ body: TestSMBBody) -> Endpoint<TestSMBAnswer> { .init(.post, "/api/vehicles/test-smb", json: body, timeout: .custom(60)) }

    // MARK: footage
    public static func days(_ id: String, category: String? = nil, offset: Int = 0, limit: Int = 30, date: String? = nil) -> Endpoint<DaysAnswer> {
        var q: [(String, String)] = []
        if let category, category != "all" { q.append(("category", category)) }
        if let date, !date.isEmpty { q.append(("date", date)) }
        q.append(("offset", String(offset)))
        q.append(("limit", String(limit)))
        return .init(.get, "/api/vehicles/\(p(id))/days", query: q)
    }
    public static func recordings(_ id: String, category: String? = nil, date: String? = nil, protectedOnly: Bool = false,
                                  offset: Int = 0, limit: Int = 500) -> Endpoint<RecordingsAnswer> {
        var q: [(String, String)] = []
        if let category, category != "all" { q.append(("category", category)) }
        if let date, !date.isEmpty { q.append(("date", date)) }
        if protectedOnly { q.append(("protected", "1")) }
        q.append(("offset", String(offset)))
        q.append(("limit", String(limit)))
        return .init(.get, "/api/vehicles/\(p(id))/recordings", query: q)
    }
    public static func recording(_ id: String, _ rid: String) -> Endpoint<Recording> { .init(.get, "/api/vehicles/\(p(id))/recordings/\(p(rid))") }
    public static func thumbnailPath(_ id: String, _ rid: String) -> String { "/api/vehicles/\(p(id))/recordings/\(p(rid))/thumbnail" }
    public static func protect(_ id: String, ids: [String], protected: Bool) -> Endpoint<ProtectAnswer> {
        .init(.post, "/api/vehicles/\(p(id))/protect", json: ProtectBody(ids: ids, protected: protected))
    }
    public static func trash(_ id: String, ids: [String]) -> Endpoint<TrashAnswer> { .init(.post, "/api/vehicles/\(p(id))/trash", json: IDsBody(ids: ids)) }
    public static func trashDays(_ id: String, _ body: TrashDaysBody) -> Endpoint<TrashDaysAnswer> { .init(.post, "/api/vehicles/\(p(id))/trash-days", json: body) }
    public static func telemetry(path: String) -> Endpoint<TelemetryAnswer> { .init(.get, "/api/telemetry", query: [("path", path)]) }
    public static func daySpeeds(_ id: String, date: String) -> Endpoint<DaySpeedsAnswer> { .init(.get, "/api/vehicles/\(p(id))/speeds", query: [("date", date)]) }
    public static func clipSpeeds(paths: [String]) -> Endpoint<ClipSpeedsAnswer> { .init(.post, "/api/telemetry/speeds", json: SpeedsBody(paths: paths)) }
    public static func speedsStatus() -> Endpoint<SpeedsStatus> { .init(.get, "/api/speeds/status") }
    public static func startSpeedIndex() -> Endpoint<SpeedsStatus> { .init(.post, "/api/speeds/index", json: [String: String]()) }
    public static func mediaPath(_ absolutePath: String) -> String { "/api/media?path=\(p(absolutePath))" }

    // MARK: policies, bin, jobs, notifications
    public static func policy(_ id: String) -> Endpoint<PolicyAnswer> { .init(.get, "/api/vehicles/\(p(id))/policy") }
    public static func setPolicy(_ id: String, _ body: PolicyBody) -> Endpoint<PolicyAnswer> { .init(.put, "/api/vehicles/\(p(id))/policy", json: body) }
    public static func runPolicy(_ id: String) -> Endpoint<TrashAnswer> { .init(.post, "/api/vehicles/\(p(id))/policy/run", json: [String: String]()) }
    public static func bin(vehicle: String? = nil) -> Endpoint<BinAnswer> {
        .init(.get, "/api/bin", query: vehicle.map { [("vehicle", $0)] } ?? [])
    }
    public static func restore(_ item: String) -> Endpoint<BinRestoreAnswer> { .init(.post, "/api/bin/\(p(item))/restore", json: [String: String]()) }
    public static func deleteForever(_ item: String) -> Endpoint<BinDeleteAnswer> {
        .init(.post, "/api/bin/\(p(item))/delete", json: ConfirmFlagBody(vehicle: nil))
    }
    public static func emptyBin(vehicle: String?) -> Endpoint<BinEmptyAnswer> { .init(.post, "/api/bin/empty", json: ConfirmFlagBody(vehicle: vehicle)) }
    public static func jobs(vehicle: String? = nil, limit: Int = 50) -> Endpoint<JobsAnswer> {
        var q: [(String, String)] = []
        if let vehicle { q.append(("vehicle", vehicle)) }
        q.append(("limit", String(limit)))
        return .init(.get, "/api/jobs", query: q)
    }
    public static func clearJobs(vehicle: String?, preview: Bool) -> Endpoint<JobsClearAnswer> {
        .init(.post, "/api/jobs/clear", json: preview ? JobsClearBody(vehicle: vehicle, preview: true) : JobsClearBody(vehicle: vehicle, confirm: true))
    }
    public static func notifications(openOnly: Bool = false, vehicle: String? = nil) -> Endpoint<NotificationsAnswer> {
        var q: [(String, String)] = []
        if openOnly { q.append(("open", "1")) }
        if let vehicle { q.append(("vehicle", vehicle)) }
        return .init(.get, "/api/notifications", query: q)
    }
    public static func dismiss(_ id: Double) -> Endpoint<OKAnswer> { .init(.post, "/api/notifications/\(JS.string(id))/dismiss", json: [String: String]()) }
    public static func markRead(_ ids: [Double]) -> Endpoint<OKAnswer> { .init(.post, "/api/notifications/read", json: NotificationIDsBody(ids: ids)) }

    // MARK: nodes
    public static func nodes() -> Endpoint<NodesAnswer> { .init(.get, "/api/nodes") }
    public static func addNode(address: String, code: String) -> Endpoint<AddNodeAnswer> { .init(.post, "/api/nodes", json: AddNodeBody(address: address, code: code)) }
    public static func nodeBenchmark(_ id: String) -> Endpoint<NodeBenchmarkAnswer> { .init(.get, "/api/nodes/\(p(id))/benchmark") }
    public static func startNodeBenchmark(_ id: String, codec: String) -> Endpoint<NodeBenchmarkAnswer> { .init(.post, "/api/nodes/\(p(id))/benchmark", json: CodecBody(codec: codec)) }
    public static func cancelNodeBenchmark(_ id: String) -> Endpoint<NodeBenchmarkAnswer> { .init(.delete, "/api/nodes/\(p(id))/benchmark") }
    public static func setNodeSlots(_ id: String, _ slots: Int) -> Endpoint<OKAnswer> { .init(.post, "/api/nodes/\(p(id))/slots", json: NodeSlotsBody(slots: Double(slots))) }
    public static func updateNode(_ id: String, _ body: NodeUpdateBody) -> Endpoint<NodeUpdateAnswer> { .init(.put, "/api/nodes/\(p(id))", json: body) }
    public static func removeNode(_ id: String) -> Endpoint<OKAnswer> { .init(.delete, "/api/nodes/\(p(id))") }
    public static func setWorker(enabled: Bool) -> Endpoint<WorkerStatus> { .init(.post, "/api/nodes/worker", json: WorkerEnableBody(enabled: enabled)) }
    public static func startPairing() -> Endpoint<WorkerStatus> { .init(.post, "/api/nodes/worker/pair", json: [String: String]()) }
    public static func cancelPairing() -> Endpoint<WorkerStatus> { .init(.delete, "/api/nodes/worker/pair") }
    public static func forgetMain(_ id: String) -> Endpoint<WorkerStatus> { .init(.delete, "/api/nodes/worker/mains/\(p(id))") }

    // MARK: studio helpers
    public static func browse(_ path: String?) -> Endpoint<BrowseAnswer> { .init(.post, "/api/studio/browse", json: BrowseBody(path: path)) }
    public static func studioContext() -> Endpoint<StudioContext> { .init(.get, "/api/studio/context") }
    public static func studioSettings() -> Endpoint<StudioSettings> { .init(.get, "/api/studio/settings") }

    // MARK: converter
    /// POST /api/converter/invoke/<name>; X-Client-Id ties progress events to this app.
    public static func invoke<V: Decodable & Sendable>(_ name: String, clientID: String, payload: Data? = nil,
                                                      as: V.Type = V.self, timeout: RequestTimeout = .normal) -> Endpoint<ConverterEnvelope<V>> {
        .init(.post, "/api/converter/invoke/\(p(name))", body: payload ?? Data(), timeout: timeout, headers: ["X-Client-Id": clientID])
    }
}
