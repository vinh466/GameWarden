using GameWarden.Core.Adapters;

namespace GameWarden.Core.Tests.Unit;

/// <summary>The first config-file format, against the Palworld template S-P1 captured (ADR-0022 decision 1).</summary>
[Trait("Category", "Unit")]
public sealed class UeStructIniTests
{
    private const string Section = "/Script/Pal.PalGameWorldSettings";
    private const string Field = "OptionSettings";

    private static string Template => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "PalworldTemplate-1.0.5.ini"));

    [Fact]
    public void The_spike_template_round_trips_field_for_field()
    {
        var fields = UeStructIni.Parse(Template, Section, Field);

        Assert.Equal(122, fields.Count);
        Assert.Equal(("Difficulty", "None"), fields[0]);
        Assert.Equal(("ServerName", "\"Default Palworld Server\""), fields.Single(field => field.Key == "ServerName"));
        Assert.Equal(("DenyTechnologyList", string.Empty), fields.Single(field => field.Key == "DenyTechnologyList"));
        Assert.Equal(Template, UeStructIni.Render(Template, Section, Field, new Dictionary<string, string>(), []));
    }

    [Fact]
    public void The_six_value_forms_are_written_as_the_game_expects()
    {
        const string template = "[/Script/Pal.PalGameWorldSettings]\r\nOptionSettings=(bOn=False,Rate=1.000000,Max=8,Mode=None,Name=\"Default\",Platforms=(Steam),Empty=)\r\n";
        var values = new List<FileFieldValue>
        {
            new("bOn", "bool", "true"),
            new("Rate", "number", "2"),
            new("Max", "int", "16"),
            new("Mode", "enum", "Hard"),
            new("Name", "string", "My Server"),
            new("Platforms", "list", "Steam,Xbox"),
            new("Empty", "string", ""),
        };

        var rendered = UeStructIni.Render(template, Section, Field, new Dictionary<string, string>(), values);

        Assert.Contains("OptionSettings=(bOn=True,Rate=2.000000,Max=16,Mode=Hard,Name=\"My Server\",Platforms=(Steam,Xbox),Empty=)", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quoted_list_writes_each_item_in_quotes()
    {
        const string template = "[/Script/Pal.PalGameWorldSettings]\r\nOptionSettings=(DenyTechnologyList=)\r\n";

        var rendered = UeStructIni.Render(
            template,
            Section,
            Field,
            new Dictionary<string, string>(),
            [new FileFieldValue("DenyTechnologyList", "list", "PALBOX,RepairBench", Quoted: true)]);

        Assert.Contains("DenyTechnologyList=(\"PALBOX\",\"RepairBench\")", rendered, StringComparison.Ordinal);
        var empty = UeStructIni.Render(template, Section, Field, new Dictionary<string, string>(), [new FileFieldValue("DenyTechnologyList", "list", "", Quoted: true)]);
        Assert.Contains("OptionSettings=(DenyTechnologyList=)", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quoted_empty_string_stays_quoted_and_an_empty_template_value_stays_empty()
    {
        const string template = "[/Script/Pal.PalGameWorldSettings]\r\nOptionSettings=(Seed=\"\",List=)\r\n";

        var rendered = UeStructIni.Render(template, Section, Field, new Dictionary<string, string>(), [new("Seed", "string", ""), new("List", "list", "")]);

        Assert.Contains("OptionSettings=(Seed=\"\",List=)", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_the_template_does_not_name_is_appended()
    {
        const string template = "[/Script/Pal.PalGameWorldSettings]\r\nOptionSettings=(A=1)\r\n";

        var rendered = UeStructIni.Render(template, Section, Field, new Dictionary<string, string> { ["B"] = "2" }, [new("C", "int", "3")]);

        Assert.Contains("OptionSettings=(A=1,B=2,C=3)", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_no_setting_names_keeps_the_template_value()
    {
        const string template = "[/Script/Pal.PalGameWorldSettings]\r\nOptionSettings=(A=1,B=\"keep\",C=3)\r\n";

        var rendered = UeStructIni.Render(template, Section, Field, new Dictionary<string, string>(), [new("C", "int", "4")]);

        Assert.Contains("OptionSettings=(A=1,B=\"keep\",C=4)", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixed_fields_are_written_before_settings()
    {
        const string template = "[/Script/Pal.PalGameWorldSettings]\r\nOptionSettings=(AdminPassword=\"old\",A=1)\r\n";

        var rendered = UeStructIni.Render(
            template,
            Section,
            Field,
            new Dictionary<string, string> { ["AdminPassword"] = "\"secret\"" },
            [new("AdminPassword", "string", "setting")]);

        Assert.Contains("AdminPassword=\"setting\"", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Commas_in_quoted_strings_and_nested_lists_do_not_split_fields()
    {
        const string template = "[/Script/Pal.PalGameWorldSettings]\r\nOptionSettings=(Name=\"A, B\",Platforms=(Steam,Xbox),B=2)\r\n";

        var fields = UeStructIni.Parse(template, Section, Field);

        Assert.Equal([("Name", "\"A, B\""), ("Platforms", "(Steam,Xbox)"), ("B", "2")], fields);
        Assert.Equal(template, UeStructIni.Render(template, Section, Field, new Dictionary<string, string>(), []));
    }

    [Theory]
    [InlineData("string", "a\"b")]
    [InlineData("string", "a\nb")]
    [InlineData("list", "Steam,\"Xbox\"")]
    [InlineData("enum", "a\rb")]
    public void Quotes_and_line_breaks_are_refused_in_a_file_value(string type, string value)
    {
        Assert.Throws<InvalidOperationException>(() => UeStructIni.Format(new FileFieldValue("F", type, value)));
    }

    [Fact]
    public void A_missing_section_or_line_says_what_is_missing()
    {
        Assert.Contains("no [nowhere] section", Assert.Throws<InvalidOperationException>(() => UeStructIni.Parse("[A]\r\nB=1\r\n", "nowhere", "B")).Message, StringComparison.Ordinal);
        Assert.Contains("no OptionSettings= line", Assert.Throws<InvalidOperationException>(() => UeStructIni.Parse("[A]\r\nB=1\r\n", "A", Field)).Message, StringComparison.Ordinal);
    }
}
