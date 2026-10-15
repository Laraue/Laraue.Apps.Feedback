# Laraue.Apps.Feedback

Internal feedback service for the Laraue.* apps. Every consuming app (`Laraue Boards` first) sends
its users' feedback - bug reports, ideas, questions - here over internal gRPC; the service stores it
and notifies the team in Telegram. See [AGENTS.md](AGENTS.md) for the design and what's
deliberately *not* built yet.

**Stage 1 (in progress, BRD-224)**: `SubmitFeedback` and a paginated `ListFeedback`, plus a Telegram
notification on every submitted feedback.

## License

Licensed under the [GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0-only), including the
`Laraue.Apps.Feedback.Internal.Contracts` NuGet package. You're free to use, modify and self-host it,
for personal or any other use, as long as you follow the AGPL - in particular, if you run a modified
version as a network service, you must make your modified source available to its users.

A commercial license without the AGPL obligations is available on request - contact the author via
[GitHub](https://github.com/win7user10).
