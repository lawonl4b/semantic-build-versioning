using NUnit.Framework;
using System;
using System.Reflection;

namespace BuildAutomation.Tests
{
    /// <summary>
    /// BuildVersionProcessor의 Semantic Versioning 로직을 검증하는 테스트
    /// </summary>
    [TestFixture]
    public class BuildVersionProcessorTests
    {
        private BuildVersionProcessor processor;
        private MethodInfo calculateNextVersionMethod;
        private MethodInfo getHighestImpactMethod;
        private MethodInfo tryParseVersionMethod;

        [SetUp]
        public void Setup()
        {
            processor = new BuildVersionProcessor();

            // Reflection을 통한 private 메소드 접근
            var type = typeof(BuildVersionProcessor);

            calculateNextVersionMethod = type.GetMethod(
                "CalculateNextVersion",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            getHighestImpactMethod = type.GetMethod(
                "GetHighestImpact",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            tryParseVersionMethod = type.GetMethod(
                "TryParseVersion",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
        }

        #region Helper Methods

        private string CallCalculateNextVersion(string currentVersion, string[] commits)
        {
            return (string)calculateNextVersionMethod.Invoke(processor, new object[] { currentVersion, commits });
        }

        private int CallGetHighestImpact(string[] commits)
        {
            return (int)getHighestImpactMethod.Invoke(processor, new object[] { commits });
        }

        private bool CallTryParseVersion(string version, out int major, out int minor, out int patch)
        {
            object[] parameters = new object[] { version, 0, 0, 0 };
            bool result = (bool)tryParseVersionMethod.Invoke(processor, parameters);
            major = (int)parameters[1];
            minor = (int)parameters[2];
            patch = (int)parameters[3];
            return result;
        }

        #endregion

        #region Version Calculation Tests

        [Test]
        [Category("VersionCalculation")]
        [Description("fix 타입 커밋은 patch 버전을 1 증가시킨다")]
        public void FixCommit_ShouldIncrementPatchVersion()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = { "fix: critical bug fixed" };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("1.2.4", nextVersion);
        }

        [Test]
        [Category("VersionCalculation")]
        [Description("feat 타입 커밋은 minor 버전을 증가시키고 patch를 0으로 리셋한다")]
        public void FeatureCommit_ShouldIncrementMinorVersionAndResetPatch()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = { "feat: add new feature" };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("1.3.0", nextVersion);
        }

        [Test]
        [Category("VersionCalculation")]
        [Description("느낌표(!)가 포함된 breaking change는 major 버전을 증가시킨다")]
        public void BreakingChangeWithExclamation_ShouldIncrementMajorVersion()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = { "feat!: breaking API change" };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("2.0.0", nextVersion);
        }

        [Test]
        [Category("VersionCalculation")]
        [Description("Footer에 BREAKING CHANGE가 있으면 major 버전을 증가시킨다")]
        public void BreakingChangeInFooter_ShouldIncrementMajorVersion()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = {
                "feat: new feature\n\nBREAKING CHANGE: removes old API"
            };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("2.0.0", nextVersion);
        }

        [Test]
        [Category("VersionCalculation")]
        [Description("여러 타입의 커밋이 있을 때 가장 높은 영향도를 적용한다")]
        public void MultipleCommitTypes_ShouldApplyHighestImpact()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = {
                "fix: minor bug",
                "docs: update README",
                "feat: add new feature",  // Highest impact
                "chore: update dependencies"
            };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("1.3.0", nextVersion, "feat has higher priority than fix");
        }

        [Test]
        [Category("VersionCalculation")]
        [Description("버전에 영향을 주지 않는 커밋만 있으면 버전이 유지된다")]
        public void NonVersionCommitsOnly_ShouldKeepCurrentVersion()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = {
                "docs: update comments",
                "style: format code",
                "chore: update gitignore"
            };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("1.2.3", nextVersion);
        }

        [Test]
        [Category("VersionCalculation")]
        [Description("scope가 포함된 커밋도 정상적으로 처리된다")]
        public void CommitsWithScope_ShouldProcessCorrectly()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = {
                "feat(ui): add new button component",
                "fix(network): connection timeout issue"
            };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("1.3.0", nextVersion, "feat has higher priority than fix");
        }

        [Test]
        [Category("VersionCalculation")]
        [Description("빈 커밋 배열은 버전을 변경하지 않는다")]
        public void EmptyCommitArray_ShouldKeepCurrentVersion()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = Array.Empty<string>();

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("1.2.3", nextVersion);
        }

        #endregion

        #region Version Parsing Tests

        [Test]
        [Category("VersionParsing")]
        [Description("올바른 형식의 버전 문자열을 정상적으로 파싱한다")]
        public void ValidVersionString_ShouldParseSuccessfully()
        {
            // Act
            bool result = CallTryParseVersion("1.2.3", out int major, out int minor, out int patch);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(1, major);
            Assert.AreEqual(2, minor);
            Assert.AreEqual(3, patch);
        }

        [Test]
        [Category("VersionParsing")]
        [Description("잘못된 형식의 버전 문자열은 파싱에 실패한다")]
        public void InvalidVersionFormat_ShouldFailToParse()
        {
            // Act
            bool result = CallTryParseVersion("1.2", out _, out _, out _);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        [Category("VersionParsing")]
        [Description("파싱할 수 없는 버전이면 0.1.0을 반환한다")]
        public void UnparsableVersion_ShouldReturn_0_1_0()
        {
            // Arrange
            string invalidVersion = "invalid.version.string";
            string[] commits = { "feat: new feature" };

            // Act
            string nextVersion = CallCalculateNextVersion(invalidVersion, commits);

            // Assert
            Assert.AreEqual("0.1.0", nextVersion);
        }

        [Test]
        [Category("VersionParsing")]
        [Description("큰 버전 번호도 올바르게 처리한다")]
        public void LargeVersionNumbers_ShouldHandleCorrectly()
        {
            // Arrange
            string currentVersion = "99.999.9999";
            string[] commits = { "fix: bug fix" };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("99.999.10000", nextVersion);
        }

        #endregion

        #region Impact Level Tests

        [Test]
        [Category("ImpactLevel")]
        [Description("Breaking change는 최고 우선순위(3)를 반환한다")]
        public void BreakingChange_ShouldReturnHighestPriority()
        {
            // Arrange
            string[] commits = {
                "feat!: breaking change",
                "feat: normal feature",
                "fix: bug fix"
            };

            // Act
            int impact = CallGetHighestImpact(commits);

            // Assert
            Assert.AreEqual(3, impact);
        }

        [Test]
        [Category("ImpactLevel")]
        [Description("Feature는 우선순위 2를 반환한다")]
        public void Feature_ShouldReturnPriorityTwo()
        {
            // Arrange
            string[] commits = {
                "feat: new feature",
                "fix: bug fix",
                "docs: update docs"
            };

            // Act
            int impact = CallGetHighestImpact(commits);

            // Assert
            Assert.AreEqual(2, impact);
        }

        [Test]
        [Category("ImpactLevel")]
        [Description("Fix는 우선순위 1을 반환한다")]
        public void Fix_ShouldReturnPriorityOne()
        {
            // Arrange
            string[] commits = {
                "fix: bug fix",
                "docs: update docs",
                "chore: update deps"
            };

            // Act
            int impact = CallGetHighestImpact(commits);

            // Assert
            Assert.AreEqual(1, impact);
        }

        [Test]
        [Category("ImpactLevel")]
        [Description("버전에 영향 없는 커밋은 우선순위 0을 반환한다")]
        public void NonVersionCommits_ShouldReturnPriorityZero()
        {
            // Arrange
            string[] commits = {
                "docs: update docs",
                "style: format code",
                "test: add tests"
            };

            // Act
            int impact = CallGetHighestImpact(commits);

            // Assert
            Assert.AreEqual(0, impact);
        }

        #endregion

        #region Edge Cases

        [Test]
        [Category("EdgeCase")]
        [Description("여러 줄로 구성된 커밋 메시지를 올바르게 처리한다")]
        public void MultilineCommitMessage_ShouldProcessCorrectly()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = {
                "feat: add new feature\n\nThis is a detailed description\nwith multiple lines"
            };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("1.3.0", nextVersion);
        }

        [Test]
        [Category("EdgeCase")]
        [Description("대소문자가 혼용된 커밋은 소문자만 인식한다")]
        public void MixedCaseCommit_ShouldOnlyRecognizeLowercase()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = {
                "FEAT: uppercase feature",  // Should be ignored
                "fix: lowercase fix"
            };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("1.2.4", nextVersion, "Uppercase FEAT should be ignored");
        }

        [Test]
        [Category("EdgeCase")]
        [Description("같은 타입의 커밋이 여러 개 있어도 버전은 한 번만 증가한다")]
        public void DuplicateCommitTypes_ShouldIncrementOnce()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = {
                "feat: feature 1",
                "feat: feature 2",
                "feat: feature 3"
            };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("1.3.0", nextVersion, "Multiple feats should only increment minor once");
        }

        [Test]
        [Category("EdgeCase")]
        [Description("빈 문자열 커밋은 무시된다")]
        public void EmptyStringCommits_ShouldBeIgnored()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = {
                "",
                "   ",
                "feat: valid feature",
                ""
            };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("1.3.0", nextVersion);
        }

        [Test]
        [Category("EdgeCase")]
        [Description("Major 버전이 99에서 100으로 넘어가는 경우를 처리한다")]
        public void MajorVersion_ShouldIncrementFrom99To100()
        {
            // Arrange
            string currentVersion = "99.5.10";
            string[] commits = { "feat!: breaking change" };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual("100.0.0", nextVersion);
        }

        #endregion

        #region Conventional Commits Compliance

        [TestCase("feat: feature", "1.3.0", TestName = "Feature commit increments minor")]
        [TestCase("feat(scope): feature", "1.3.0", TestName = "Feature with scope increments minor")]
        [TestCase("feat(ui): feature", "1.3.0", TestName = "Feature with ui scope increments minor")]
        [TestCase("fix: bug", "1.2.4", TestName = "Fix commit increments patch")]
        [TestCase("fix(core): bug", "1.2.4", TestName = "Fix with scope increments patch")]
        [TestCase("feat!: breaking", "2.0.0", TestName = "Breaking feature increments major")]
        [TestCase("fix!: breaking fix", "2.0.0", TestName = "Breaking fix increments major")]
        [TestCase("refactor: code", "1.2.3", TestName = "Refactor doesn't change version")]
        [TestCase("docs: documentation", "1.2.3", TestName = "Docs don't change version")]
        [TestCase("style: formatting", "1.2.3", TestName = "Style doesn't change version")]
        [TestCase("test: add tests", "1.2.3", TestName = "Test doesn't change version")]
        [TestCase("chore: maintenance", "1.2.3", TestName = "Chore doesn't change version")]
        [Category("ConventionalCommits")]
        [Description("Conventional Commits 규칙을 준수하는지 검증한다")]
        public void ConventionalCommits_ShouldFollowSemanticVersioning(string commit, string expectedVersion)
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = { commit };

            // Act
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);

            // Assert
            Assert.AreEqual(expectedVersion, nextVersion, $"Commit '{commit}' failed version calculation");
        }

        #endregion

        #region Performance Tests

        [Test]
        [Category("Performance")]
        [Description("대량의 커밋을 합리적인 시간 내에 처리한다")]
        public void LargeCommitSet_ShouldProcessInReasonableTime()
        {
            // Arrange
            string currentVersion = "1.2.3";
            string[] commits = new string[1000];
            for (int i = 0; i < 1000; i++)
            {
                commits[i] = i % 3 == 0 ? "feat: feature" :
                            i % 3 == 1 ? "fix: bug" :
                            "docs: documentation";
            }

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string nextVersion = CallCalculateNextVersion(currentVersion, commits);
            stopwatch.Stop();

            // Assert
            Assert.AreEqual("1.3.0", nextVersion);
            Assert.Less(stopwatch.ElapsedMilliseconds, 100,
                "Processing 1000 commits should take less than 100ms");
        }

        #endregion
    }
}