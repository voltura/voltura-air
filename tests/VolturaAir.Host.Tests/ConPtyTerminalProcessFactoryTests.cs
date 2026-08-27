using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace VolturaAir.Host.Tests;

public sealed partial class ConPtyTerminalProcessFactoryTests
{
    [Fact]
    public async Task RealConPtyRunsTheUserShellAndJobCloseKillsItsChildTree()
    {
        ITerminalProcess process = new ConPtyTerminalProcessFactory().Start(80, 24);
        try
        {
            const string command = "$child = Start-Process powershell.exe -WindowStyle Hidden -ArgumentList '-NoProfile -Command Start-Sleep 300' -PassThru; Write-Output \"VAIR_CHILD:$($child.Id)\"\r";
            await process.Input.WriteAsync(Encoding.UTF8.GetBytes(command));
            await process.Input.FlushAsync();

            using var output = new StreamReader(process.Output, Encoding.UTF8, leaveOpen: true);
            var captured = new StringBuilder();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            int childId = 0;
            while (childId == 0)
            {
                char[] buffer = new char[1024];
                int read = await output.ReadAsync(buffer, timeout.Token);
                Assert.NotEqual(0, read);
                captured.Append(buffer, 0, read);
                Match match = ChildProcessMarker().Match(captured.ToString());
                if (match.Success) childId = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }

            using Process child = Process.GetProcessById(childId);
            process.Terminate();
            _ = await process.ExitCode.WaitAsync(TimeSpan.FromSeconds(10));
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(child.HasExited);
        }
        finally
        {
            await process.DisposeAsync();
        }
    }

    [GeneratedRegex("VAIR_CHILD:(?<id>[0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ChildProcessMarker();
}
