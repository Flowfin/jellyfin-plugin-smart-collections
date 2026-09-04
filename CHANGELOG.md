# Changelog

All notable changes to this plugin are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

There has been no release. Everything below sits under Unreleased until there
is one, and the versioning policy that decides what a version number means is
still being written.

## [Unreleased]

### Fixed

- The shipped package is built against `Jellyfin.Controller` and `Jellyfin.Model`
  `10.11.0`, the floor `build.yaml` promises as `targetAbi`, instead of `10.11.11`,
  so the assembly binds at the version a `10.11.0` server carries and an install
  at the floor loads it. The suite runs at the same pin
  ([#105](https://github.com/Flowfin/jellyfin-plugin-smart-collections/issues/105)).

### Added

- The compiler takes the instant an evaluation runs at as an argument, so the
  engine reads no clock and two relative conditions in one rule end their spans
  at one instant. `premiereDate withinLast` compiles to the floor and the
  ceiling the server's query carries for a premiere date; `dateAdded withinLast`
  is handed back, because the query carries no ceiling for the date the server
  first saw an item and a floor alone would select more than the rule says.
- The pull request hygiene check refuses a closing keyword under a negation next
  to an issue reference, in the body and in every commit message. GitHub reads
  the pair rather than the sentence, so a change explaining that it leaves an
  issue open used to shut it.
- The collection a rule owns is renamed when the name in the rule document
  changes, rather than keeping the title it was created with. The rule document
  is the declaration, so a collection renamed in the Jellyfin interface is
  renamed back on the next refresh.
- A test project, so the required test check runs an assertion instead of
  reporting success over an empty suite.
- A check that the plugin's identifier agrees across the manifest, the plugin
  class and the configuration page.
- Repository documents: this changelog, a code of conduct, a security policy
  with the plugin's standing scope statement, issue templates and a pull
  request template.
- A front page describing what the plugin does, which server lines it targets
  and what a rule document looks like.

### Changed

- Both manifests declare release notes written for this plugin rather than the
  Jellyfin plugin template's own word `changelog`. The packaging tool copies that
  value verbatim into the metadata beside the archive and into the repository
  manifest a server reads, so it is what a catalogue shows an operator before they
  install, and every check in front of the packaging step is satisfied by the word.
- The plugin advertises its own identifier rather than the one every copy of the
  Jellyfin plugin template ships with.
- The CodeQL workflow scans under this repository's name rather than the
  template's.
- The three refusals in the rule language reference that were put as a question
  before they were written down name the question they were decided on and the
  day, rather than saying the question has no answer recorded. Every question on
  #67 was answered on 2026-08-24 and none of the three against the wording that
  was standing, so what changed is what a reader is told about where the refusal
  came from rather than the refusal.
- The version both manifests declare is `0.1.0.0` rather than the template's
  `1.0.0.0`, which is the number the first release carries. The assemblies are
  stamped with the same number, so the release route no longer stops on the
  comparison between the two after a tag has been spent.
