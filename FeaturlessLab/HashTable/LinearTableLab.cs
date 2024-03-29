// ReSharper disable RedundantUsingDirective
// ReSharper disable FieldCanBeMadeReadOnly.Local
// ReSharper disable CollectionNeverQueried.Local
// ReSharper disable RedundantDefaultMemberInitializer
// ReSharper disable ArrangeTypeModifiers
// ReSharper disable NotAccessedField.Local
// ReSharper disable InconsistentNaming
// ReSharper disable RedundantJumpStatement
// ReSharper disable EmptyConstructor


#pragma warning disable CS8618
#pragma warning disable CS0169


namespace FeaturlessLab.HashTable;


#region

using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Featurless;
using Featurless.HashTable;
using Featurless.MiniTest;
#endregion

// ReSharper disable TabsAreDisallowed
//==================================================================//
//	TableType   	DataType	Load%	Miss%	Benchmark	Time	//
//==================================================================//
//	Dictionary  	ref     	25  	0   	Build   	00ms	//
//	Dictionary  	ref     	100 	0   	Build   	00ms	//
//	Dictionary  	value   	25  	0   	Build   	00ms	//
//	Dictionary  	value   	100 	0   	Build   	00ms	//
//                                                                  //
//	Dictionary  	ref     	25  	0   	Update  	00ms	//
//	Dictionary  	value   	25  	0   	Update  	00ms	//
//                                                                  //
//	Dictionary  	ref     	25  	0   	Probe   	00ms	//
//	Dictionary  	ref     	25  	100 	Probe   	00ms	//
//	Dictionary  	value   	25  	0   	Probe   	00ms	//
//	Dictionary  	value   	25  	100 	Probe   	00ms	//
//                                                                  //
//	Dictionary  	ref     	25  	0   	Clear   	00ms	//
//	Dictionary  	ref     	25  	100 	Clear   	00ms	//
//	Dictionary  	value   	25  	0   	Clear   	00ms	//
//	Dictionary  	value   	25  	100 	Clear   	00ms	//
//============------------------------------------------============//
//	LinearTable 	ref     	25  	0   	Build   	00ms	//
//	LinearTable 	ref     	100 	0   	Build   	00ms	//
//	LinearTable 	value   	25  	0   	Build   	00ms	//
//	LinearTable 	value   	100 	0   	Build   	00ms	//
//                                                                  //
//	LinearTable 	ref     	25  	0   	Update  	00ms	//
//	LinearTable 	value   	25  	0   	Update  	00ms	//
//                                                                  //
//	LinearTable 	ref     	25  	0   	Probe   	00ms	//
//	LinearTable 	ref     	25  	100 	Probe   	00ms	//
//	LinearTable 	value   	25  	0   	Probe   	00ms	//
//	LinearTable 	value   	25  	100 	Probe   	00ms	//
//                                                                  //
//	LinearTable 	ref     	25  	0   	Clear   	00ms	//
//	LinearTable 	ref     	25  	100 	Clear   	00ms	//
//	LinearTable 	value   	25  	0   	Clear   	00ms	//
//	LinearTable 	value   	25  	100 	Clear   	00ms	//
//==================================================================//
// ReSharper restore TabsAreDisallowed

struct BigType
{
    public long x1;
    public long x2;
    public long x3;
    public long x4;
    public long x5;
    public long x6;
    public long x7;
    public long x8;

    public BigType() {
        x1 = 12;
        x2 = x1 * 5;
        x3 = x2 - x1;
        x4 = x1 + x3 - x2;
        x5 = x1 + x2 + x4;
        x6 = x2 * 7;
        x7 = x3 + x6;
        x8 = x7 / x1;
    }
}

class LinearTableBuildStruct
{
    private const int _maxCapacity = 1000000;
    private LinearTable<int, BigType> _lt = new(LinearTableBuildStruct._maxCapacity);

    public LinearTableBuildStruct() { }

    public void Build25() {
        _lt.Clear();
        for (int i = 0; i < LinearTableBuildStruct._maxCapacity / 4; ++i) {
            _lt.Add(i, new BigType());
        }
    }
}

public static class LinearTableLab
{
    public static void Run(string[] args, MiniTest tests) {
        LinearTableLab.Test(tests);
        if (!tests.StatusOk("LinearTable")) {
            Console.WriteLine("Unable to perform linear table benchmark, tests not passed.");
            return;
        }
        /* *
        Summary? summary = BenchmarkRunner.Run<BenchmarkLinearTableStruct>();
        if (summary != null) {
            Console.WriteLine(summary);
        }
        /*/
        // BenchmarkLinearTableStruct blts = new();
        /* */
    }

    public static void Test(MiniTest tests) {
        const string keyTest = "testkey";
        const int valueTest = 13;
        LinearTable<string, int> lt = new(1000);
        tests.Require(groupName: "LinearTable", testName: "Initial capacity ok", lt.Capacity == 1000);
        tests.Require(groupName: "LinearTable", testName: "Initial count ok", lt.Count == 0);
        tests.Check(groupName: "LinearTable", testName: "Initial keys ok",
                    lt.Keys.Count == 0 && lt.Keys.FirstOrDefault() == null);
        tests.Check(groupName: "LinearTable", testName: "Initial values ok",
                    lt.Values.Count == 0 && lt.Values.FirstOrDefault() == 0);
        tests.Require(groupName: "LinearTable", testName: "ContainsKey Failure before adding",
                      !lt.ContainsKey(keyTest));
        tests.Check(groupName: "LinearTable", testName: "Contains Failure before adding",
                    !lt.Contains(new KeyValuePair<string, int>(keyTest, value: 12)));
        lt.Add(keyTest, valueTest);
        tests.Require(groupName: "LinearTable", testName: "Added element ok", lt.Capacity == 1000 && lt.Count == 1);
        tests.Require(groupName: "LinearTable", testName: "ContainsKey success", lt.ContainsKey(keyTest));
        tests.Require(groupName: "LinearTable", testName: "ContainsKey success with diff ref",
                      lt.ContainsKey("testkey"));
        tests.Check(groupName: "LinearTable", testName: "Contains success",
                    lt.Contains(new KeyValuePair<string, int>(keyTest, valueTest)));
        tests.Check(groupName: "LinearTable", testName: "Indexer get", lt[keyTest] == valueTest);
        lt[keyTest] = valueTest * 2;
        tests.Check(groupName: "LinearTable", testName: "Indexer set", lt[keyTest] == valueTest * 2);
        lt.Remove(new KeyValuePair<string, int>(keyTest, value: 0));
        tests.Require(groupName: "LinearTable", testName: "ContainsKey Failure after remove", !lt.ContainsKey(keyTest));
        tests.Check(groupName: "LinearTable", testName: "Contains Failure after remove",
                    !lt.Contains(new KeyValuePair<string, int>(keyTest, value: 12)));
        tests.Require(groupName: "LinearTable", $"Count down after remove({lt.Count})", lt.Count == 0);
        lt.Add(keyTest, valueTest);
        lt.Add(key: "thekey", valueTest * 2);
        lt.Add(key: "otherkey", valueTest * 4);
        lt.Add(key: "innerkey", valueTest * 8);
        lt.Add(key: "outerkey", valueTest * 16);
        lt.Add(key: "oldkey", valueTest * 32);
        lt.Add(key: "rustedkey", valueTest * 64);
        lt.Add(key: "goldkey", valueTest * 128);
        tests.Check(groupName: "LinearTable", testName: "Count up multiple times", lt.Count == 8);
        tests.Require(groupName: "LinearTable", testName: "Multiple items contained success"
                      , lt.ContainsKey(keyTest) && lt.ContainsKey("thekey") && lt.ContainsKey("otherkey")
                     && lt.ContainsKey("innerkey") && lt.ContainsKey("outerkey") && lt.ContainsKey("oldkey")
                     && lt.ContainsKey("rustedkey") && lt.ContainsKey("goldkey"));
        lt.TryGetValue(key: "thekey", out int tgValue);
        tests.Check(groupName: "LinearTable", testName: "TryGetValue", tgValue == valueTest * 2);
        ref int valueRef = ref lt.GetValueRef("innerkey");
        tests.Check(groupName: "LinearTable", testName: "GetValueRef get ref", valueRef == valueTest * 8);
        valueRef = 17;
        tests.Check(groupName: "LinearTable", testName: "GetValueRef set ref", lt["innerkey"] == 17);
        ICollection<KeyValuePair<string, int>> icoll = lt;
        var kv = new KeyValuePair<string, int>[lt.Count + 1];
        kv[0] = new KeyValuePair<string, int>(key: "nothing", value: 0);
        icoll.CopyTo(kv, arrayIndex: 1);
        bool successCopy = kv[0].Key == "nothing" && kv[0].Value == 0;
        int i = 1;
        foreach (KeyValuePair<string, int> pair in icoll) {
            successCopy &= kv[i].Key == pair.Key && kv[i].Value == pair.Value;
            ++i;
        }

        tests.Check(groupName: "LinearTable", testName: "ICollection CopyTo", successCopy);
        lt.Clear();
        tests.Check(groupName: "LinearTable", testName: "Cleared",
                    lt.Count == 0 && !lt.ContainsKey(keyTest) && !lt.ContainsKey("thekey")
                 && !lt.ContainsKey("otherkey") && !lt.ContainsKey("innerkey")
                 && !lt.ContainsKey("outerkey") && !lt.ContainsKey("oldkey")
                 && !lt.ContainsKey("rustedkey") && !lt.ContainsKey("goldkey"));
    }
}

#pragma warning restore CS8618
#pragma warning restore CS0169
