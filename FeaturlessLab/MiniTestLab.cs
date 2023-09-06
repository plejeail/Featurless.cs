namespace FeaturlessLab;


using Featurless.MiniTest;


public static class MiniTestLab
{
    public static void TestGroup1(MiniTest tests, string groupName) {
        tests.Check(groupName, $"This {groupName} test is successfull", value: true);
        tests.Check(groupName, $"This {groupName} test fails", value: false);
        tests.Require(groupName, $"This {groupName} test is required and successful", value: true);
    }

    public static void TestGroup2(MiniTest tests, string groupName) {
        tests.Check(groupName, $"Another success for {groupName}", value: true);
        tests.Check(groupName, $"Another failure for {groupName}", value: false);
        tests.Require(groupName, $"This {groupName} test is required and fails", expression: () => false);
        tests.Check(groupName, $"This {groupName} test never happens", expression: () => true);
        tests.Check(groupName, $"This {groupName} one also never happens", value: false);
    }

    public static void Run(string[] args) {
        using FileStream fs = File.OpenWrite("./mini-test-output.txt");
        MiniTest tests = new(args, fs) { SuccessColor = ConsoleColor.Green, ErrorColor = ConsoleColor.Magenta, };
        tests.MaxWidth = 80;
        MiniTestLab.TestGroup1(tests, groupName: "toto");
        MiniTestLab.TestGroup2(tests, groupName: "toto");
        tests.Check(testName: "This test is ok", value: true);
        tests.Check(testName: "This test is a failure", value: false);
        tests.Require(testName: "This test is required and successful", expression: () => true);
        tests.Check(testName: "This test is another ok", expression: () => true);
        tests.Check(testName: "This test is another failure", expression: () => false);
        MiniTestLab.TestGroup1(tests, groupName: "tata");
        tests.Require(testName: "This test is required but fail", value: false);
        tests.Check(testName: "This test never happens", value: true);
        tests.Check(testName: "This one also never happens", value: false);
        MiniTestLab.TestGroup2(tests, groupName: "tata");
        MiniTestLab.TestGroup1(tests, groupName: "titi");
        MiniTestLab.TestGroup2(tests, groupName: "titi");
        tests.Summarize();
    }
}
