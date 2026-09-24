# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## 0.2.0
- New plugin id `pl.info.lukaszm.plugins.ridertestssupportplus`: Rider treats it as a different plugin, uninstall versions up to 0.1.2 first
- Author contact and project URL in the plugin metadata, MIT license

## 0.1.2
- Plugin metadata: author, display name and description

## 0.1.1
- The import report is an information, not a warning, when every test was resolved and nothing went wrong

## 0.1.0
- Save and load Unit Tests sessions (`.rtsession`), including parametrized tests such as `TestClass(A).TestName(X)`, resolved by name
- Loading survives a changed project GUID or target framework; missing tests trigger a rescan, then fall back to their parent, and are reported
- Import a session from `.runsettings`: `RunConfiguration/TestCaseFilter` (evaluated by `dotnet vstest`) or `NUnit/Where` (evaluated by the NUnit engine)
- Background task with progress and cancellation for import and rescan
