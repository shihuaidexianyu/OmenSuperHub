using System.Diagnostics;

namespace OmenSuperHub {
  internal sealed class ProcessResult {
    public int ExitCode { get; set; }
    public string Output { get; set; }
    public string Error { get; set; }
  }

  internal sealed class ProcessCommandService {
    public ProcessResult Execute(string command) {
      var processStartInfo = new ProcessStartInfo {
        FileName = "cmd.exe",
        Arguments = $"/c {command}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden
      };

      using (var process = new Process { StartInfo = processStartInfo }) {
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult {
          ExitCode = process.ExitCode,
          Output = output,
          Error = error
        };
      }
    }
  }
}
