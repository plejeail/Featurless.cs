namespace FeaturlessLab;

using Featurless;

public static class MiniTestLab
{
    public static void TestGroup1(MiniTest tests, string groupName) {
        tests.Check(groupName, $"This {groupName} test is successfull", true);
        tests.Check(groupName, $"This {groupName} test fails", false);
        tests.Require(groupName, $"This {groupName} test is required and successful", true);
    }

    public static void TestGroup2(MiniTest tests, string groupName) {
        tests.Check(groupName, $"Another success for {groupName}", true);
        tests.Check(groupName, $"Another failure for {groupName}", false);
        tests.Require(groupName, $"This {groupName} test is required and fails", () => false);
        tests.Check(groupName, $"This {groupName} test never happens", () => true);
        tests.Check(groupName, $"This {groupName} one also never happens", false);
    }

    public static void Run(string[] args) {
        using FileStream fs = File.OpenWrite("./mini-test-output.txt");
        MiniTest tests = new(args, fs) {
                SuccessColor = ConsoleColor.Green,
                ErrorColor = ConsoleColor.Magenta,
        };

        tests.MaxWidth = 80;
        TestGroup1(tests, "toto");
        TestGroup2(tests, "toto");

        tests.Check("This test is ok", true);
        tests.Check("This test is a failure", false);

        tests.Require("This test is required and successful", () => true);
        tests.Check("This test is another ok", () => true);
        tests.Check("This test is another failure", () => false);

        TestGroup1(tests, "tata");

        tests.Require("This test is required but fail", false);
        tests.Check("This test never happens", true);
        tests.Check("This one also never happens", false);

        TestGroup2(tests, "tata");
        TestGroup1(tests, "titi");
        TestGroup2(tests, "titi");

        tests.Summarize();
    }
}
