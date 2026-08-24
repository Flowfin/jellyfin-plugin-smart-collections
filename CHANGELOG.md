# Changelog

All notable changes to this plugin are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

There has been no release. Everything below sits under Unreleased until there
is one, and the versioning policy that decides what a version number means is
still being written.

## [Unreleased]

### Added

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

- The plugin advertises its own identifier rather than the one every copy of the
  Jellyfin plugin template ships with.
- The CodeQL workflow scans under this repository's name rather than the
  template's.
- The version both manifests declare is `0.1.0.0` rather than the template's
  `1.0.0.0`, which is the number the first release carries. The assemblies are
  stamped with the same number, so the release route no longer stops on the
  comparison between the two after a tag has been spent.
