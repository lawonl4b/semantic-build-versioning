using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Semantic Versioning을 자동으로 적용하고 Git 태그를 생성하는 빌드 프로세서
/// </summary>
public class BuildVersionProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    // 빌드 프로세스 간 데이터 공유
    private static string originalVersion;
    private static string nextVersion;
    private static bool versionChanged = false;

    // 성능 최적화: Regex를 static으로 선언
    private static readonly Regex BreakingChangeRegex = new Regex(@"^[a-z]+\(?[a-z]*\)?!:", RegexOptions.Compiled);

    public int callbackOrder => 0;

    /// <summary>
    /// 빌드가 시작되기 직전에 호출되는 메소드
    /// </summary>
    public void OnPreprocessBuild(BuildReport report)
    {
        originalVersion = PlayerSettings.bundleVersion;
        versionChanged = false;

        // Git 저장소 확인
        if (!IsGitRepository())
        {
            Debug.LogWarning("[AutoVersion] Git 저장소가 아닙니다. 버전 자동 업데이트를 건너뜁니다.");
            nextVersion = originalVersion;
            return;
        }

        // 마지막 태그 가져오기
        string lastTag = GetLastTag();
        if (string.IsNullOrEmpty(lastTag))
        {
            Debug.Log("Git 태그를 찾을 수 없습니다. 현재 버전으로 빌드합니다.");
            nextVersion = originalVersion;
            return;
        }

        // 태그 이후의 커밋들 가져오기
        var (success, commits) = GetCommitsSinceTag(lastTag);
        if (!success)
        {
            Debug.LogError("커밋 내역을 가져오는데 실패했습니다. 현재 버전으로 빌드합니다.");
            nextVersion = originalVersion;
            return;
        }

        // 커밋이 없으면 버전 변경 없음
        if (commits.Length == 0)
        {
            Debug.Log("새로운 커밋이 없어 버전 변경 없이 빌드를 진행합니다.");
            nextVersion = originalVersion;
            return;
        }

        // 다음 버전 계산
        nextVersion = CalculateNextVersion(originalVersion, commits);

        // 버전 변경 적용
        if (originalVersion != nextVersion)
        {
            versionChanged = true;
            PlayerSettings.bundleVersion = nextVersion;
            AssetDatabase.SaveAssets();
            Debug.Log($"버전 업데이트: {originalVersion} -> {nextVersion}");
        }
        else
        {
            Debug.Log("버전 변경사항이 없습니다. 현재 버전으로 빌드합니다.");
        }
    }

    /// <summary>
    /// 빌드가 완료된 직후에 호출되는 메소드
    /// </summary>
    public void OnPostprocessBuild(BuildReport report)
    {
        if (!versionChanged) return;

        // 빌드 결과 확인
        bool buildSucceeded = IsBuildSuccessful(report);

        if (buildSucceeded)
        {
            Debug.Log($"빌드 성공! (결과물 경로: {report.summary.outputPath})");
            CreateAndPushGitTag();
        }
        else
        {
            Debug.LogWarning("빌드 실패! 프로젝트 버전을 롤백합니다.");
            RollbackVersion();
        }
    }

    #region Git Operations

    /// <summary>
    /// Git 저장소 여부 확인
    /// </summary>
    private bool IsGitRepository()
    {
        var (exitCode, _, _) = RunCommand("git", "rev-parse --git-dir");
        return exitCode == 0;
    }

    /// <summary>
    /// 마지막 Git 태그 가져오기
    /// </summary>
    private string GetLastTag()
    {
        // 1순위: 가장 최근 태그
        var (exitCode, result, _) = RunCommand("git", "describe --tags --abbrev=0");
        if (exitCode == 0 && !string.IsNullOrEmpty(result))
        {
            return result;
        }

        // 2순위: v로 시작하는 태그 중 가장 최근
        (exitCode, result, _) = RunCommand("git", "tag -l 'v*' --sort=-version:refname");
        if (exitCode == 0 && !string.IsNullOrEmpty(result))
        {
            return result.Split('\n')[0].Trim();
        }

        // 3순위: 첫 커밋
        (exitCode, result, _) = RunCommand("git", "rev-list --max-parents=0 HEAD");
        return exitCode == 0 ? result : string.Empty;
    }

    /// <summary>
    /// 특정 태그 이후의 커밋 메시지들 가져오기
    /// </summary>
    private (bool success, string[] commits) GetCommitsSinceTag(string tag)
    {
        var (exitCode, result, error) = RunCommand("git", $"log {tag.Trim()}..HEAD --pretty=format:%B|||");

        if (exitCode != 0)
        {
            Debug.LogError($"커밋 로그 가져오기 실패: {error}");
            return (false, Array.Empty<string>());
        }

        var commits = result.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(c => c.Trim())
                           .Where(c => !string.IsNullOrEmpty(c))
                           .ToArray();

        return (true, commits);
    }

    /// <summary>
    /// Git 태그 생성 및 푸시
    /// </summary>
    private void CreateAndPushGitTag()
    {
        string tagName = $"v{nextVersion}";

        // 태그 중복 체크
        var (exitCode, existingTag, _) = RunCommand("git", $"tag -l {tagName}");
        if (!string.IsNullOrEmpty(existingTag))
        {
            Debug.LogWarning($"태그 '{tagName}'가 이미 존재합니다. 태그 생성을 건너뜁니다.");
            return;
        }

        // 태그 생성
        var (tagExitCode, _, tagError) = RunCommand("git", $"tag -a {tagName} -m \"Release {tagName}\"");
        if (tagExitCode != 0)
        {
            Debug.LogError($"태그 생성 실패: {tagError}");
            return;
        }

        Debug.Log($"Git 태그 '{tagName}' 생성 완료!");

        // 태그 푸시
        var (pushExitCode, _, pushError) = RunCommand("git", "push origin --tags");
        if (pushExitCode != 0)
        {
            Debug.LogError($"태그 푸시 실패: {pushError}\n수동으로 'git push origin --tags'를 실행해주세요.");
        }
        else
        {
            Debug.Log($"Git 태그 '{tagName}' 푸시 완료!");
        }
    }

    #endregion

    #region Version Calculation

    /// <summary>
    /// Semantic Versioning에 따라 다음 버전 계산
    /// </summary>
    private string CalculateNextVersion(string currentVersion, string[] commitMessages)
    {
        if (!TryParseVersion(currentVersion, out int major, out int minor, out int patch))
        {
            Debug.LogWarning($"현재 버전({currentVersion}) 형식이 잘못되어 0.1.0으로 시작합니다.");
            return "0.1.0";
        }

        int impact = GetHighestImpact(commitMessages);

        switch (impact)
        {
            case 3: // Breaking Change
                major++;
                minor = 0;
                patch = 0;
                break;
            case 2: // Feature
                minor++;
                patch = 0;
                break;
            case 1: // Fix
                patch++;
                break;
            default:
                // 변경사항 없음
                break;
        }

        return $"{major}.{minor}.{patch}";
    }

    /// <summary>
    /// 버전 문자열 파싱
    /// </summary>
    private bool TryParseVersion(string version, out int major, out int minor, out int patch)
    {
        major = minor = patch = 0;

        try
        {
            var parts = version.Split('.').Select(int.Parse).ToArray();
            if (parts.Length < 3) return false;

            major = parts[0];
            minor = parts[1];
            patch = parts[2];
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 커밋 메시지에서 가장 높은 영향도 계산
    /// 3 = Breaking Change (major), 2 = Feature (minor), 1 = Fix (patch), 0 = No change
    /// </summary>
    private int GetHighestImpact(string[] commitMessages)
    {
        int highestImpact = 0;

        foreach (var msg in commitMessages)
        {
            string header = msg.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                              .FirstOrDefault() ?? "";

            // Breaking Change 체크 (최우선)
            if (BreakingChangeRegex.IsMatch(header) || msg.Contains("BREAKING CHANGE:"))
            {
                return 3; // 즉시 반환
            }

            // Feature 체크
            if (header.StartsWith("feat"))
            {
                highestImpact = Math.Max(highestImpact, 2);
            }
            // Fix 체크
            else if (header.StartsWith("fix"))
            {
                highestImpact = Math.Max(highestImpact, 1);
            }
        }

        return highestImpact;
    }

    #endregion

    #region Build Validation

    /// <summary>
    /// 빌드 성공 여부 확인
    /// </summary>
    private bool IsBuildSuccessful(BuildReport report)
    {
        // 1. BuildResult 체크 - Cancelled와 Failed만 실패로 간주
        if (report.summary.result == BuildResult.Cancelled ||
            report.summary.result == BuildResult.Failed)
        {
            Debug.Log($"BuildResult: {report.summary.result}");
            return false;
        }

        // 2. 빌드 결과물 존재 여부 체크
        string outputPath = report.summary.outputPath;
        if (string.IsNullOrEmpty(outputPath))
        {
            Debug.LogWarning("빌드 결과물 경로가 비어있습니다.");
            return false;
        }

        // 플랫폼별 검증
        switch (report.summary.platform)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
            case BuildTarget.StandaloneOSX:
            case BuildTarget.StandaloneLinux64:
                // 실행 파일 확인
                return System.IO.File.Exists(outputPath);

            case BuildTarget.Android:
                // .apk 또는 .aab 파일 확인
                return System.IO.File.Exists(outputPath) &&
                       (outputPath.EndsWith(".apk") || outputPath.EndsWith(".aab"));

            case BuildTarget.iOS:
                // Xcode 프로젝트 폴더 확인
                return System.IO.Directory.Exists(outputPath);

            case BuildTarget.WebGL:
                // WebGL 빌드 폴더 확인 (index.html 필수)
                return System.IO.Directory.Exists(outputPath) &&
                       System.IO.File.Exists(System.IO.Path.Combine(outputPath, "index.html"));

            default:
                // 기본: 파일 또는 폴더 존재 확인
                return System.IO.File.Exists(outputPath) || System.IO.Directory.Exists(outputPath);
        }
    }

    /// <summary>
    /// 버전 롤백
    /// </summary>
    private void RollbackVersion()
    {
        PlayerSettings.bundleVersion = originalVersion;
        AssetDatabase.SaveAssets();
        Debug.Log($"버전 롤백 완료: {nextVersion} -> {originalVersion}");
    }

    #endregion

    #region Command Execution

    /// <summary>
    /// 터미널 명령어 실행
    /// </summary>
    private (int exitCode, string output, string error) RunCommand(string command, string args)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Application.dataPath + "/..", // Unity 프로젝트 루트
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                return (process.ExitCode, output.Trim(), error.Trim());
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"명령어 실행 중 예외 발생: {command} {args}\n{ex.Message}");
            return (-1, string.Empty, ex.Message);
        }
    }

    #endregion
}