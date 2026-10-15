# AGENTS.md

Guidance for AI agents working in this repo. Human-readable conventions and gotchas that aren't
obvious from the code alone.

## What this project is

The internal feedback service for the Laraue.* ecosystem. Every Laraue app (`Laraue Boards` first)
sends the feedback its users leave - a bug report, an idea, a question - here, and this service
stores it and notifies the team in a Telegram chat. It is internal on purpose: users never call it
directly, the app they're in does, over gRPC.

**Stage 1 (BRD-224, in progress)**: internal gRPC only (`Protos/feedback.proto`, `FeedbackService`).
- `SubmitFeedback(user_id, type, message, context)` -> feedback id, stored as `New`. Requires the
  `x-laraue-service-id` header, a non-empty user id, a set type, a message that isn't blank after
  trimming and fits `FeedbackItem.MessageMaxLength`, and a context within `FeedbackItem.Context*`
  limits (entries, key and value length) - otherwise `INVALID_ARGUMENT`. The message is stored
  trimmed.
- `ListFeedback(service?, status?, page, per_page)` -> a page, newest first, with `has_next_page`
  (`ShortPaginateEFAsync`). `page` is 0-based, `per_page` 1-`FeedbackService.PerPageMax` (100);
  the core service checks this itself, since `Laraue.Core`'s pagination throws a plain
  `ArgumentException` (a gRPC `INTERNAL`) for a bad page. It doesn't read the service header: listing
  is for reading everyone's feedback, not tied to the caller.
- A Telegram notification (**not built yet**, next step) to the team chat on every submitted feedback: type, sender, service,
  page, message. The sender is resolved through `Laraue.Apps.Identity`'s `GetUserProfile` (display
  name, `@username` or the Google email - BRD-223). If Identity is unavailable, the notification
  still goes out, with the global user id instead of a name - a failed lookup never loses feedback.

**Not built yet - future stages, don't add speculatively**:
- Screenshots/files attached to feedback (BRD-227, after the files service).
- Replying to users, changing a feedback's status, a web API/UI for reading feedback.

## Domain model

- `FeedbackItem` (table `feedback_items`) - one piece of feedback: the global Laraue user id
  (`Guid`, from Identity), the service it came from, its type (`Bug` / `Idea` / `Question`), the
  message, the context, its status (`New` / `Seen` / `Done`) and when it was created. Stage 1 only
  ever writes `New` - status changes are a later stage. Named `FeedbackItem`, not `Feedback`: a type
  called `Feedback` is shadowed by the `Laraue.Apps.Feedback` namespace in every project here, so
  each use would need qualifying. The message is at most `FeedbackItem.MessageMaxLength` (3000): the
  notification carries the whole message plus a few header lines, and a Telegram message is at most
  4096 characters. "Newest first" is `ORDER BY id DESC` (an identity column), served by the primary key -
  no `created_at` index.
- `FeedbackItem.Context` - free-form key/value pairs the calling app attaches (page URL,
  organization, app version, locale, browser), a `Dictionary<string, string>` stored as `jsonb`.
  This service doesn't interpret them, except that the notification shows the page. Serialized by
  Npgsql's dynamic JSON, which needs `ConfigureDataSource(x => x.EnableDynamicJson())` in the host's
  `UseNpgsql` (see `InternalApiHost/Program.cs`) - a new host without it fails at the first insert
  with "requires dynamic JSON serialization".
- `Service` - a consuming app (`LaraueBoards`), seeded via `HasData` from `Data/ServicesData.cs`,
  same as Identity. An independent registry from Identity's/Billing's own `ServiceId` enums - adding
  a caller means a new enum value, a `ServicesData` entry, a migration and a matching value in the
  proto's `ServiceId`.
- The calling service is identified by the `x-laraue-service-id` gRPC header, same as Identity
  (see Identity's `GrpcHeaders`/`ServiceIdInterceptor`), not by a request field - so a caller can't
  submit feedback on behalf of another service by mistake.

## Project layout

Solution: `Laraue.Apps.Feedback.sln`. Structured the same way as `Laraue.Apps.Identity` (see that
repo's `AGENTS.md` for the fuller rationale) - `Host -> Host{Services} -> Services`, a host never
referencing `Services` directly, only through its own `Host{Services}` project.

- `src/Laraue.Apps.Feedback.DataAccess` - EF Core `DatabaseContext`, entities, migrations.
- `src/Laraue.Apps.Feedback.Internal.Contracts` - the `.proto` service-to-service contract plus its
  generated stubs, built with `GrpcServices="Both"` so one package ships both the client stub (for
  callers like `Laraue.Apps.Boards`) and the server base class. `IsPackable=true` - published to
  NuGet.org as `Laraue.Apps.Feedback.Internal.Contracts` via `.github/workflows/nuget-publish.yml`
  (manual trigger; `main` publishes the base `<Version>` from `Directory.Build.props`, any other
  branch appends `-alpha.<run number>`). No DB/ASP.NET dependencies.
- `src/Laraue.Apps.Feedback.Services` - core business logic, not tied to gRPC: `FeedbackService`
  (submit with validation, listing). Invalid input is a `BadRequestException` with an
  `Errors.resx` message.
- `src/Laraue.Apps.Feedback.InternalApiServices` - `FeedbackGrpcService`, the gRPC-facing
  implementation of `Internal.Contracts`, mapping wire types <-> domain types and
  `BadRequestException` -> `INVALID_ARGUMENT` (with the field errors as the status detail -
  the exception's own `Message` is just "Bad request"), plus its own DI composition
  (`AddInternalApiServices()`). The generated proto types share names with the domain ones
  (`FeedbackService`, `ServiceId`, `FeedbackType`, ...), so the file aliases them as
  `Contracts.`/`Domain.`.
- `src/Laraue.Apps.Feedback.InternalApiHost` - the gRPC host: `Program.cs` only (Kestrel h2c, DI,
  OpenTelemetry, migrations on startup). Listens on **two separate ports** (`Kestrel:GrpcPort`/
  `:HealthPort`, 5463/5464 by default - Identity uses 5363/5364, Billing 5263/5264): one
  HTTP/2-only for gRPC, one HTTP/1.1-only for `/_health`/`/_metrics`. Kestrel can't serve HTTP/1.1
  and cleartext HTTP/2 on one endpoint without TLS - a shared `Http1AndHttp2` endpoint silently
  downgrades to HTTP/1.1 and breaks every gRPC call. See Identity's `AGENTS.md` for the full story;
  don't "simplify" it back to one port.
- `tests/Laraue.Apps.Feedback.IntegrationTests` - a `WebApplicationFactory<Program>` host for
  `InternalApiHost` that hands back the real generated gRPC client over the in-memory `TestServer`,
  so tests call the contract the way another Laraue app would. Same per-test pattern as Identity:
  `using var testScope = host.CreateTestScope();` cleans the database, test classes are
  `[Collection("IntegrationTest")]`. The Telegram bot client and the Identity client are mocked.

## External services

- **Identity** (`Laraue.Apps.Identity.Internal.Contracts` NuGet package) - only `GetUserProfile`, to
  name the sender in the notification. Treat it as optional at runtime: a timeout or an RPC error
  means "send with the user id", never "fail the submit".
- **Telegram** - the bot token and the team chat id come from config. Sending the notification
  must not fail the submit either: the feedback is saved first, then the team is notified.

## Options and configuration

Follow `Laraue.Apps.Boards`'s convention: bind every options class through a validated-options
helper (validation and `ValidateOnStart()` in Production only), mark required values `[Required]`,
and register an options class only in the hosts that use it. Secrets (bot token) never go into a
committed `appsettings.json` - production values come from the deploy server's environment.

## No unpaginated lists

Every gRPC method that returns a collection whose size depends on data is paginated - never return
a whole table. `ListFeedback` is paginated from the start, newest first.

## EF Core

- Snake_case naming convention (`.UseSnakeCaseNamingConvention()`), same as Identity/Billing.
- Timestamps are UTC `DateTime` taken from `IDateTimeProvider` (`Laraue.Core.DateTime`), not
  `DateTime.UtcNow`.
- Project straight to the needed shape with `.Select(...)` instead of loading entities and mapping
  them - same rule as Boards' "Query shape" section.

## User-facing text

Don't put string literals directly in `throw new SomeException("...")` calls. Use
`Resources/Errors.resx` (+ hand-maintained `Errors.Designer.cs` - `dotnet build` doesn't regenerate
it on this machine; add the matching property by hand, mirroring an existing entry).

## Logging

Always use `ILogger<T>`, never the bare non-generic `ILogger`.

## Metrics

Prometheus metrics on `InternalApiHost`'s `/_metrics` (health port), `System.Diagnostics.Metrics`,
meter `Laraue.Apps.Feedback`. Same conventions as Identity's/Billing's "Metrics" sections: dotted
`feedback.<noun>.<verb or state>` names, counters are events recorded after the row is saved, labels
are low-cardinality (service, type, status) - never a user or feedback id.

## Database safety

Migrations run automatically on startup (`InternalApiHost` and the integration test host both call
`MigrateAsync()`/`Migrate()`), so there's normally no need for `dotnet ef database update` by hand.
**Never run `dotnet ef database drop`** - `--startup-project` determines which `appsettings.json`
connection string a command targets, and it doesn't necessarily point at the test database (dev
`feedback` vs. test `feedback_tests`). Confirm which database a destructive command targets before
running it, and ask first if there's any ambiguity.

## Build-lock protocol

`dotnet build` can fail with `MSB3026`/`MSB3027` file-lock errors if `InternalApiHost` is already
running locally and holding the output DLLs open. Don't kill the process yourself - ask the user to
stop it, then retry the build once they confirm.

## Deploy

Same shape as Identity's `.github/workflows/dotnet.yml`: the `deploy` job (push to `main` only)
packs `InternalApiHost`'s publish output, uploads it to `/home/laraue/feedback-internal-api-host` on
the deploy server and rebuilds/restarts its `docker compose` service there. The compose service
block, the database and the GitHub environment/secrets (`SSH_*`, `TELEGRAM_BOT_TOKEN`,
`TELEGRAM_CHAT_ID`) are set up once by a human - they aren't in this repo.

## Testing

Naming convention: `{Handler}_Should{ExpectedBehavior}_When{Condition}`, matching Identity/Billing/
Boards. Prefer a separate `[Fact]`/`[Theory]` per case over one test covering several scenarios.
Assert on observable behavior (the gRPC response, the stored row, what the mocked bot client was
asked to send) - not on what got logged.

## Workflow for new features

Don't implement a whole feature in one shot. Move step-by-step, starting from the data model
(entities/migration) and building up (services, gRPC, notification, tests). Stop after each step and
let the user review before continuing - don't pile up a large diff.

## Task flow

Create branch with pattern `feature/task-number-task-description`, e.g.
`feature/BRD-224-feedback-service`, matching Identity/Billing/Boards.
