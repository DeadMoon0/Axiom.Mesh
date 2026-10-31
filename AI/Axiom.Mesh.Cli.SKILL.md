<identity>
    <package>Axiom.Mesh.Cli</package>
    <role>addon-skill</role>
    <requires>Axiom.Mesh.Local.SKILL.md</requires>
</identity>

<objective>
    Teach an agent the command line axiom-mesh: its commands, how it reaches the Host, and how it explains failures.
</objective>

<package_scope>
    Namespace Axiom.Mesh.Cli. Public: CliProgram.RunAsync(args, output, error, CliEnvironment, ct) -> CliExitCode (Ok 0, Usage 1, HostNotReachable 2, Refused 3, Timeout 4), CliEnvironment { Version, ConnectHost (stream factory to the pipe), HostAddress, ReadConfig, ConfigFile, Variables, OutputIsTerminal, ErrorIsTerminal, Time, RequestTimeout 30 s, CurrentDirectory, WriteFile }, CliPaths.ConfigFile(platform, environment) (the Host's home rule, copied). Internal: Parsing/ (CommandSpecs.All, the table of every command; CommandLine.Parse -> ParsedCommand | UsageError; CommandSpec, ArgumentSpec, OptionSpec, ParsedArguments), Commands/ (one record per command with RunAsync(CommandContext)), Running/ (CommandContext: the user connection and the cli service connection opened on first use, SendAsync/SendAsServiceAsync with the request deadline, CliFailure; IdResolver over the list requests with the pure IdMatch; OperationRunner for Fetch; BytesProgress; PairingWatch over topic pairing; CliConfig reading services.cli.token with JsonDocument), Output/ (pure views: TextTable, Formats, StatusText, ExitCodes, EntrySummary, JsonOut with Utf8JsonWriter, Users/Peers/Packages/History/Store/Status/Pairing views). Commands: status, version, help, identity create, pair show|join|confirm|cancel|status, users, devices, peers, sessions, dial, peer add|remove, package create|list|show|follow|unfollow|publish|materialise, grant set|revoke, write, history, entry, object, watch, store, pins, pin, unpin; host install|uninstall answer "not available yet" (M13).
</package_scope>

<architecture>
    A user client of Axiom.Mesh.Local only (no System.CommandLine, plan decision D9). Program is the only shell (console, environment, disk, Ctrl-C); everything else is a function of CliEnvironment. Most commands connect as the user; write and package publish connect as the external service cli (decision D4) with AXIOM_MESH_SERVICE_TOKEN or services.cli.token from config.json, register their kind, and write: a Message via WriteEntry.Of(package, MessagePayload.ToBody()) (Axiom.Mesh.Payloads; the Host parses no payload, the envelope's references and redacts come from ToBody); a folder via Trees.PublishDirectoryAsync (Local helpers: Chunks, ChunkLists and Trees put with PutObject), WriteEntry a Version whose references name the root Tree, then Unpin of every object put (LOCAL-SPEC 9.5). package materialise reads the newest Version with ListHistory (reverse, kind Version, limit 1), runs Fetch (recursive) on its Tree so what is missing comes from the peers, then StoreFiles.MaterialiseAsync with Existing fail|replace|merge on this side of the pipe (Materialise and PutFile left the Host in LOCAL-SPEC draft 4). Pair commands subscribe to topic pairing before they act and wait for the state they need: pair show and pair join until the SAS shows, pair confirm until the pairing ended. watch subscribes to entries from HelloAck.storeSeq + 1 and resubscribes after a lapse. Packed as the .NET tool axiom-mesh; published as Native AOT by Scripts/aot-smoke.ps1|.sh (the command line with the ILCompiler argument --noscan, STRUCTURAL-DEBT 47).
</architecture>

<best_practices>
    Add a command as a row of CommandSpecs.All and a record in Commands/; keep parsing and output pure and test them in Cli.Tests/Rules; every known failure leaves as CliFailure (one line, one exit code); ids accept full hex or an unambiguous prefix (IdMatch), Packages and users also their exact name; --json on every list via Utf8JsonWriter (no reflection). Test end to end with TestNode.CliAsync against a TestMesh whose Local options configure the cli service.
</best_practices>

<pitfalls>
    Never reference Core or Host (ServiceOrAppConventionTests); CliPaths must stay equal to the Host's HostPaths (Host.Tests: CliHomeRegressionTests). Never print a stack trace for a known fault. FollowPackage needs the Package listed first (connect and settle before following in tests). The token is read per run; a Host that never started has written none. In PowerShell scripts do not name a function Cli (an alias of Clear-Item).
</pitfalls>
