// The Core/Workspace DTOs (UploadedFile, ProjectAnalysis, WebBundleResult, BundleStats, …) are referenced
// throughout the web tests; importing them globally keeps each test file focused on its subject.
global using ScadBundler.Core.Workspace;
