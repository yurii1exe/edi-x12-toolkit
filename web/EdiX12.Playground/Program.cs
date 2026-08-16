using EdiX12.Playground;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// No HttpClient is registered, deliberately. Once the app has booted it makes no network
// requests at all, which is the property the page claims on its face. The samples are
// embedded resources, and parsing happens in the browser's WebAssembly runtime.
await builder.Build().RunAsync();
