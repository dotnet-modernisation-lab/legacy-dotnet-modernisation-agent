using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace LegacyDotNetModernisationAgent.Behaviour;

/// <summary>
/// Starts one IIS Express site for the probe and stops only the process it started.
/// It never kills by image name, so it cannot disturb the user's own IIS Express instances.
/// </summary>
public sealed class IisExpressHost : IDisposable
{
    private readonly Process process;

    private IisExpressHost(Process process, int port, string name)
    {
        this.process = process;
        Port = port;
        Name = name;
    }

    public int Port { get; }

    public string Name { get; }

    public Uri BaseAddress => new($"http://localhost:{Port}/");

    public static string? FindExecutable() =>
        new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "IIS Express", "iisexpress.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "IIS Express", "iisexpress.exe")
        }.FirstOrDefault(File.Exists);

    public static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public static int FindFreePort(int preferred)
    {
        for (var port = preferred; port < preferred + 50; port++)
        {
            if (IsPortFree(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException($"No free port found from {preferred}.");
    }

    public static IisExpressHost Start(string name, string sitePath, int port)
    {
        var executable = FindExecutable()
            ?? throw new FileNotFoundException("IIS Express (iisexpress.exe) was not found; install it with Visual Studio.");

        if (!IsPortFree(port))
        {
            throw new InvalidOperationException($"Port {port} needed by {name} is already in use; stop whatever is using it and retry.");
        }

        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        info.ArgumentList.Add($"/path:{Path.GetFullPath(sitePath)}");
        info.ArgumentList.Add($"/port:{port}");

        var process = Process.Start(info)!;

        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return new IisExpressHost(process, port, name);
    }

    public async Task WaitUntilReadyAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new InvalidOperationException($"{Name} exited during start-up (exit code {process.ExitCode}).");
            }

            try
            {
                using var response = await client.GetAsync(new Uri(BaseAddress, path), cancellationToken);

                if ((int)response.StatusCode < 500)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException($"{Name} did not become ready on port {Port} within {timeout.TotalSeconds:0}s.");
    }

    public void Stop()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
    }

    public void Dispose()
    {
        Stop();
        process.Dispose();
    }
}
