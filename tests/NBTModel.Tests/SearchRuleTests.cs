using NBTExplorer.Model;
using NBTExplorer.Model.Search;
using Substrate.Nbt;
using Xunit;

namespace NBTModel.Tests;

/// <summary>
/// SearchRule.cs is reused unchanged by the new inline rule builder — each UI row is a thin
/// ViewModel writing straight through to one of these rule objects. These tests pin the
/// semantics that the dropdowns will expose.
/// </summary>
public class SearchRuleTests
{
    private static TagCompoundDataNode Container()
    {
        var node = new TagCompoundDataNode(new TagNodeCompound {
            ["SpawnX"] = new TagNodeInt(128),
            ["Health"] = new TagNodeFloat(20.0f),
            ["LevelName"] = new TagNodeString("Nether Hub"),
            ["Hardcore"] = new TagNodeByte(1),
        });
        node.Expand();
        return node;
    }

    [Theory]
    [InlineData(NumericOperator.Equals, 128, true)]
    [InlineData(NumericOperator.Equals, 127, false)]
    [InlineData(NumericOperator.NotEquals, 127, true)]
    [InlineData(NumericOperator.NotEquals, 128, false)]
    [InlineData(NumericOperator.GreaterThan, 127, true)]
    [InlineData(NumericOperator.GreaterThan, 128, false)]
    [InlineData(NumericOperator.LessThan, 129, true)]
    [InlineData(NumericOperator.LessThan, 128, false)]
    [InlineData(NumericOperator.Any, 0, true)]
    public void IntRuleHonoursEveryNumericOperator(NumericOperator op, long value, bool expected)
    {
        var rule = new IntTagRule { Name = "SpawnX", Operator = op, Value = value };
        var matched = new List<TagDataNode>();

        Assert.Equal(expected, rule.Matches(Container(), matched));
        if (expected)
            Assert.Single(matched);
    }

    [Theory]
    [InlineData(StringOperator.Equals, "Nether Hub", true)]
    [InlineData(StringOperator.Equals, "Nether", false)]
    [InlineData(StringOperator.NotEquals, "Overworld", true)]
    [InlineData(StringOperator.Contains, "ther", true)]
    [InlineData(StringOperator.Contains, "Overworld", false)]
    [InlineData(StringOperator.NotContains, "Overworld", true)]
    [InlineData(StringOperator.StartsWith, "Nether", true)]
    [InlineData(StringOperator.StartsWith, "Hub", false)]
    [InlineData(StringOperator.EndsWith, "Hub", true)]
    [InlineData(StringOperator.Any, "", true)]
    public void StringRuleHonoursEveryStringOperator(StringOperator op, string value, bool expected)
    {
        var rule = new StringTagRule { Name = "LevelName", Operator = op, Value = value };
        Assert.Equal(expected, rule.Matches(Container(), []));
    }

    [Fact]
    public void FloatRuleComparesAsDouble()
    {
        Assert.True(new FloatTagRule { Name = "Health", Operator = NumericOperator.Equals, Value = 20.0 }
                    .Matches(Container(), []));
        Assert.True(new FloatTagRule { Name = "Health", Operator = NumericOperator.LessThan, Value = 20.5 }
                    .Matches(Container(), []));
        Assert.False(new FloatTagRule { Name = "Health", Operator = NumericOperator.GreaterThan, Value = 20.0 }
                     .Matches(Container(), []));
    }

    [Fact]
    public void RuleAgainstAMissingTagDoesNotMatch()
    {
        Assert.False(new IntTagRule { Name = "NoSuchTag", Operator = NumericOperator.Any }
                     .Matches(Container(), []));
    }

    [Fact]
    public void RuleAgainstTheWrongTagTypeDoesNotMatch()
    {
        // LevelName is a string, so an IntTagRule's LookupTag<TagNodeInt> returns null.
        Assert.False(new IntTagRule { Name = "LevelName", Operator = NumericOperator.Any }
                     .Matches(Container(), []));
    }

    [Fact]
    public void WildcardRuleDispatchesOnTheActualTagType()
    {
        Assert.True(new WildcardRule { Name = "SpawnX", Operator = WildcardOperator.Equals, Value = "128" }
                    .Matches(Container(), []));
        Assert.True(new WildcardRule { Name = "LevelName", Operator = WildcardOperator.Equals, Value = "Nether Hub" }
                    .Matches(Container(), []));
        Assert.True(new WildcardRule { Name = "Hardcore", Operator = WildcardOperator.NotEquals, Value = "0" }
                    .Matches(Container(), []));
        Assert.False(new WildcardRule { Name = "SpawnX", Operator = WildcardOperator.Equals, Value = "999" }
                     .Matches(Container(), []));
    }

    [Fact]
    public void IntersectRuleRequiresEveryChildAndUnionRequiresOne()
    {
        var hit = new IntTagRule { Name = "SpawnX", Operator = NumericOperator.Equals, Value = 128 };
        var miss = new IntTagRule { Name = "SpawnX", Operator = NumericOperator.Equals, Value = 0 };

        var all = new IntersectRule();
        all.Rules.Add(hit);
        all.Rules.Add(miss);
        Assert.False(all.Matches(Container(), []));

        var any = new UnionRule();
        any.Rules.Add(hit);
        any.Rules.Add(miss);
        Assert.True(any.Matches(Container(), []));
    }

    [Fact]
    public void GroupsNestArbitrarilyDeep()
    {
        // "SpawnX = 128 AND (LevelName contains 'Nether' OR Health > 100)"
        var inner = new UnionRule();
        inner.Rules.Add(new StringTagRule { Name = "LevelName", Operator = StringOperator.Contains, Value = "Nether" });
        inner.Rules.Add(new FloatTagRule { Name = "Health", Operator = NumericOperator.GreaterThan, Value = 100 });

        var root = new RootRule();
        root.Rules.Add(new IntTagRule { Name = "SpawnX", Operator = NumericOperator.Equals, Value = 128 });
        root.Rules.Add(inner);

        Assert.True(root.Matches(Container(), []));
        Assert.True(root.CanAddRules);
    }

    [Fact]
    public void AnEmptyRootRuleMatchesEverything()
    {
        // RootRule is an IntersectRule, so vacuous truth. The UI must not let an empty rule set
        // run a replace-all across a world.
        Assert.True(new RootRule().Matches(Container(), []));
    }

    [Fact]
    public void MatchedNodesAreCollectedWithoutDuplicates()
    {
        var matched = new List<TagDataNode>();
        var root = new RootRule();
        root.Rules.Add(new IntTagRule { Name = "SpawnX", Operator = NumericOperator.Any });
        root.Rules.Add(new IntTagRule { Name = "SpawnX", Operator = NumericOperator.Equals, Value = 128 });

        Assert.True(root.Matches(Container(), matched));
        Assert.Single(matched);
        Assert.Equal("SpawnX", matched[0].NodeName);
    }

    [Fact]
    public void OperatorDisplayStringsCoverEveryEnumValue()
    {
        // The new rule builder binds its ComboBoxes straight to these dictionaries, so a missing
        // entry would be a KeyNotFoundException at render time rather than a compile error.
        foreach (NumericOperator op in Enum.GetValues<NumericOperator>())
            Assert.True(SearchRule.NumericOpStrings.ContainsKey(op), $"NumericOpStrings missing {op}");
        foreach (StringOperator op in Enum.GetValues<StringOperator>())
            Assert.True(SearchRule.StringOpStrings.ContainsKey(op), $"StringOpStrings missing {op}");
        foreach (WildcardOperator op in Enum.GetValues<WildcardOperator>())
            Assert.True(SearchRule.WildcardOpStrings.ContainsKey(op), $"WildcardOpStrings missing {op}");
    }
}
