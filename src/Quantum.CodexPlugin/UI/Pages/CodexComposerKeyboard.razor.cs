using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Quantum.OfficialPlugins.Codex.UI.Pages;

public partial class CodexComposerKeyboard
{
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private ElementReference _marker;
    private IJSObjectReference? _module;
    private IJSObjectReference? _binding;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/quantum.plugin.codex/codex-composer.js");
            if (_module is not null)
            {
                _binding = await _module.InvokeAsync<IJSObjectReference>("attach", _marker);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_binding is not null)
            {
                await _binding.InvokeVoidAsync("dispose");
                await _binding.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
