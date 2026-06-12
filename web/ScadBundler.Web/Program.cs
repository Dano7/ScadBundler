using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ScadBundler.Web;
using ScadBundler.Web.State;

namespace ScadBundler.Web;

/// <summary>
/// The Blazor WebAssembly entry point. Mounts <see cref="App"/>, registers the single
/// <see cref="WorkspaceController"/> that owns all UI state and drives the Core/Workspace facade, and runs
/// the host. All bundling work happens in-browser; no file content crosses the network.
/// </summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        // Scoped == singleton in a WASM app: one controller per browser session (Design §3.2).
        builder.Services.AddScoped<WorkspaceController>();

        await builder.Build().RunAsync();
    }
}
