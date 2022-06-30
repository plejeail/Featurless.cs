// ReSharper disable RedundantUsingDirective
// ReSharper disable FieldCanBeMadeReadOnly.Local
// ReSharper disable CollectionNeverQueried.Local
// ReSharper disable RedundantDefaultMemberInitializer
// ReSharper disable ArrangeTypeModifiers
// ReSharper disable NotAccessedField.Local


namespace FeaturlessLab.HashTable;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Featurless;
using FeaturlessLab.HashTable;

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
//==================================================================//
struct BigStruct
{
    private const int _gridSize = 14;
    private static long _denseKey = 0;
    private static Random _rnd = new();
    private static long _dtTicks = DateTime.UtcNow.Ticks;
    private long _1;
    private long _2;
    private long _3;
    private long _4;
    private long _5;
    private long _6;
    private long _7;
    private long _8;

    public BigStruct() {
        _1 = _dtTicks / 1;
        _2 = _dtTicks / 2;
        _3 = _dtTicks / 3;
        _4 = _dtTicks / 4;
        _5 = _dtTicks / 5;
        _6 = _dtTicks / 6;
        _7 = _dtTicks / 7;
        _8 = _dtTicks / 8;
    }

    public static long GenerateDenseKey() {
        return _denseKey++;
    }

    public static long GenerateRndKey() {
        return _rnd.Next();
    }

    public static long GenerateGridKey() {
        const int divMax = Int32.MaxValue / _gridSize;
        int x1 = _rnd.Next() / divMax;
        int x2 = _rnd.Next() / divMax;
        int x3 = _rnd.Next() / divMax;
        int x4 = _rnd.Next() / divMax;
        int key = x1;
        key |= x2 << 0x08;
        key |= x3 << 0x10;
        key |= x4 << 0x18;
        return key;
    }
}

class Reference
{
    private const int _gridSize = 14;
    private static long _dtTicks = DateTime.UtcNow.Ticks;
    private static long _denseKey = 0;
    private static Random _rnd = new();

    private long _1;
    private long _2;
    private long _3;
    private long _4;
    private long _5;
    private long _6;
    private long _7;
    private long _8;

    public Reference() {
        _1 = _dtTicks / 1;
        _2 = _dtTicks / 2;
        _3 = _dtTicks / 3;
        _4 = _dtTicks / 4;
        _5 = _dtTicks / 5;
        _6 = _dtTicks / 6;
        _7 = _dtTicks / 7;
        _8 = _dtTicks / 8;
    }

    public static long GenerateDenseKey() {
        return _denseKey++;
    }

    public static long GenerateRndKey() {
        return _rnd.Next();
    }

    public static long GenerateGridKey() {
        const int divMax = Int32.MaxValue / _gridSize;
        int x1 = _rnd.Next() / divMax;
        int x2 = _rnd.Next() / divMax;
        int x3 = _rnd.Next() / divMax;
        int x4 = _rnd.Next() / divMax;
        int key = x1;
        key |= x2 << 0x08;
        key |= x3 << 0x10;
        key |= x4 << 0x18;
        return key;
    }
}

public class BenchmarkLinearTableStruct
{
    private const int _totalSize = 1_000;

    private LinearTable<long, BigStruct>? _lt;

    [Params(25, 100)]public int LoadFactor;

    [Params(0, 100)]public int MissPercent;

    [Benchmark]
    public void PopulateDense() {
        _lt = new LinearTable<long, BigStruct>(_totalSize);
        int nbEntries = LoadFactor * _totalSize / 100;
        for (int i = 0; i < nbEntries; ++i) {
            _lt.Add(BigStruct.GenerateDenseKey(), new BigStruct());
        }
    }

    [Benchmark]
    public void PopulateGrid() {
        _lt = new LinearTable<long, BigStruct>(_totalSize);
        int nbEntries = LoadFactor * _totalSize / 100;
        for (int i = 0; i < nbEntries; ++i) {
            _lt.Add(BigStruct.GenerateGridKey(), new BigStruct());
        }
    }

    [Benchmark]
    public void PopulateRnd() {
        _lt = new LinearTable<long, BigStruct>(_totalSize);
        int nbEntries = LoadFactor * _totalSize / 100;
        for (int i = 0; i < nbEntries; ++i) {
            _lt.Add(BigStruct.GenerateRndKey(), new BigStruct());
        }
    }
}

public static class LinearTableLab
{
    public static void Run(string[] args, MiniTest tests) {
        const string keyTest = "testkey";
        const int valueTest = 13;
        LinearTable<string, int> lt = new(1000);
        tests.Require("LinearTable", "Initial capacity ok", lt.Capacity == 1000);
        tests.Require("LinearTable", "Initial count ok", lt.Count == 0);
        tests.Check("LinearTable", "Initial keys ok", lt.Keys.Count == 0 && lt.Keys.FirstOrDefault() == null);
        tests.Check("LinearTable", "Initial values ok", lt.Values.Count == 0 && lt.Values.FirstOrDefault() == 0);

        tests.Require("LinearTable", "ContainsKey Failure before adding", !lt.ContainsKey(keyTest));
        tests.Check("LinearTable", "Contains Failure before adding", !lt.Contains(new KeyValuePair<string, int>(keyTest, 12)));
        lt.Add(keyTest, valueTest);
        tests.Require("LinearTable", "Added element ok", lt.Capacity == 1000 && lt.Count == 1);

        tests.Require("LinearTable", "ContainsKey success", lt.ContainsKey(keyTest));
        tests.Require("LinearTable", "ContainsKey success with diff ref", lt.ContainsKey("testkey"));
        tests.Check("LinearTable", "Contains success", lt.Contains(new KeyValuePair<string, int>(keyTest, valueTest)));
        tests.Check("LinearTable", "Indexer get", lt[keyTest] == valueTest);
        lt[keyTest] = valueTest * 2;
        tests.Check("LinearTable", "Indexer set", lt[keyTest] == valueTest * 2);
        lt.Remove(new KeyValuePair<string, int>(keyTest, 0));
        tests.Require("LinearTable", "ContainsKey Failure after remove", !lt.ContainsKey(keyTest));
        tests.Check("LinearTable", "Contains Failure after remove", !lt.Contains(new KeyValuePair<string, int>(keyTest, 12)));
        tests.Require("LinearTable", $"Count down after remove({lt.Count})", lt.Count == 0);

        lt.Add(keyTest, valueTest);
        lt.Add("thekey", valueTest*2);
        lt.Add("otherkey", valueTest*4);
        lt.Add("innerkey", valueTest*8);
        lt.Add("outerkey", valueTest*16);
        lt.Add("oldkey", valueTest * 32);
        lt.Add("rustedkey", valueTest * 64);
        lt.Add("goldkey", valueTest * 128);

        tests.Check("LinearTable", "Count up multiple times", lt.Count == 8);
        tests.Require("LinearTable", "Multiple items contained success"
                    , lt.ContainsKey(keyTest) && lt.ContainsKey("thekey") && lt.ContainsKey("otherkey")
                   && lt.ContainsKey("innerkey") && lt.ContainsKey("outerkey") && lt.ContainsKey("oldkey")
                   && lt.ContainsKey("rustedkey") && lt.ContainsKey("goldkey"));



        // TryGetValue
        // ICollection<KeyValuePair<TKey, TValue>>.CopyTo
        // Clear (then recheck contains)
        // GetValueRef + update ref => change value

        // Enumerator: MoveNext until end, must have gone through al key/value pairs must end at the end
        // KeyCollection/ValueCollection
        // - Count is same as lineartable
        // - Test enumerator, sme way as above
        // Collectin
        /* *
        Summary? summary = BenchmarkRunner.Run<BenchmarkLinearTableStruct>();
        if (summary != null) {
            Console.WriteLine(summary);
        }
        /*/
        // BenchmarkLinearTableStruct blts = new();
        /* */
    }
}
