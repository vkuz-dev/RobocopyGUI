using System.Linq;
using RobocopyGui.Command;
using Xunit;

namespace RobocopyGui.Tests
{
    public class CommandLineTests
    {
        [Theory]
        [InlineData("C:\\Source", "C:\\Source")]
        [InlineData("C:\\My Folder", "\"C:\\My Folder\"")]
        [InlineData("C:\\My Folder\\", "\"C:\\My Folder\\\\\"")]
        [InlineData("\\\\server\\share", "\\\\server\\share")]
        [InlineData("", "\"\"")]
        [InlineData("a\"b", "\"a\\\"b\"")]
        public void Quote_round_trips_through_tokenizer(string value, string expectedRaw)
        {
            string raw = CommandLine.Quote(value);
            Assert.Equal(expectedRaw, raw);

            string trailing;
            var tokens = CommandLine.Tokenize(raw, out trailing);
            Assert.Single(tokens);
            Assert.Equal(value, tokens[0].Value);
        }

        [Fact]
        public void Tokenize_keeps_raw_text_and_whitespace()
        {
            string text = "robocopy   \"C:\\A B\"  D:\\X\t/E ";
            string trailing;
            var tokens = CommandLine.Tokenize(text, out trailing);

            Assert.Equal(new[] { "robocopy", "C:\\A B", "D:\\X", "/E" }, tokens.Select(t => t.Value));
            Assert.Equal("\"C:\\A B\"", tokens[1].Raw);
            Assert.Equal("  ", tokens[2].Leading);
            Assert.Equal(" ", trailing);
            Assert.Equal(text, RobocopyCommand.Parse(text).ToString());
        }
    }

    public class RobocopyCommandTests
    {
        [Fact]
        public void Parses_positional_arguments_and_file_specs()
        {
            var c = RobocopyCommand.Parse("robocopy \"C:\\Source\" \"D:\\Destination\" *.docx *.pdf /E /MT:32");
            Assert.True(c.HasExecutable);
            Assert.Equal("C:\\Source", c.Source);
            Assert.Equal("D:\\Destination", c.Destination);
            Assert.Equal(new[] { "*.docx", "*.pdf" }, c.FileSpecs);
            Assert.Equal("32", c.GetSwitchArgument("/MT"));
        }

        [Fact]
        public void Values_after_multi_value_switches_are_not_file_specs()
        {
            var c = RobocopyCommand.Parse("robocopy C:\\S D:\\D /XF *.tmp *.bak /E extra.txt /XD Temp");
            Assert.Equal(new[] { "*.tmp", "*.bak" }, c.GetValues("/XF"));
            Assert.Equal(new[] { "Temp" }, c.GetValues("/XD"));
            // Robocopy treats plain arguments after an ordinary switch as file specs.
            Assert.Equal(new[] { "extra.txt" }, c.FileSpecs);
        }

        [Fact]
        public void Changing_thread_count_keeps_unknown_switches_verbatim()
        {
            const string text = "robocopy \"C:\\Source\" \"D:\\Destination\" /E /MT:32 /Z /R:2 /MYCUSTOMOPTION /my_custom_switch:\"x y\"";
            var c = RobocopyCommand.Parse(text);
            c.SetSwitch("/MT", "8");
            Assert.Equal("robocopy \"C:\\Source\" \"D:\\Destination\" /E /MT:8 /Z /R:2 /MYCUSTOMOPTION /my_custom_switch:\"x y\"", c.ToString());
        }

        [Fact]
        public void Flags_are_added_at_the_end_and_removed_in_place()
        {
            var c = RobocopyCommand.Parse("robocopy a b /E /CUSTOM /Z");
            c.SetSwitch("/MIR", true);
            c.SetSwitch("/Z", false);
            Assert.Equal("robocopy a b /E /CUSTOM /MIR", c.ToString());
            c.SetSwitch("/MIR", true); // idempotent
            Assert.Equal("robocopy a b /E /CUSTOM /MIR", c.ToString());
        }

        [Fact]
        public void Switch_names_are_case_insensitive()
        {
            var c = RobocopyCommand.Parse("robocopy a b /mt:4 /z");
            Assert.Equal("4", c.GetSwitchArgument("/MT"));
            Assert.True(c.HasSwitch("/Z"));
            c.SetSwitch("/MT", null);
            Assert.Equal("robocopy a b /z", c.ToString());
        }

        [Fact]
        public void Setting_paths_inserts_placeholders_and_quotes()
        {
            var c = RobocopyCommand.Parse(RobocopyCommand.DefaultCommand);
            Assert.Null(c.Source);

            c.SetPositional(1, "D:\\Back up");
            Assert.Equal("robocopy \"\" \"D:\\Back up\" " + RobocopyCommand.DefaultOptions, c.ToString());

            c.SetPositional(0, "\\\\SERVER\\Data");
            Assert.Equal("robocopy \\\\SERVER\\Data \"D:\\Back up\" " + RobocopyCommand.DefaultOptions, c.ToString());
        }

        [Fact]
        public void File_specs_are_written_after_destination()
        {
            var c = RobocopyCommand.Parse("robocopy a b /E /CUSTOM");
            c.SetFileSpecs(new[] { "*.docx", "My File*.xlsx" });
            Assert.Equal("robocopy a b *.docx \"My File*.xlsx\" /E /CUSTOM", c.ToString());
            c.SetFileSpecs(new string[0]);
            Assert.Equal("robocopy a b /E /CUSTOM", c.ToString());
        }

        [Fact]
        public void Excluded_files_replace_all_xf_values()
        {
            var c = RobocopyCommand.Parse("robocopy a b /XF *.tmp /E /XF *.old /CUSTOM");
            c.SetValues("/XF", v => true, new[] { "*.bak", "~$*" });
            Assert.Equal("robocopy a b /XF *.bak ~$* /E /CUSTOM", c.ToString());
            c.SetValues("/XF", v => true, new string[0]);
            Assert.Equal("robocopy a b /E /CUSTOM", c.ToString());
        }

        [Fact]
        public void Removing_a_switch_does_not_let_a_file_spec_join_a_preceding_xd()
        {
            var c = RobocopyCommand.Parse("robocopy a b /XD Temp /MIR *.txt /E");
            c.SetSwitch("/MIR", false);
            Assert.Equal(new[] { "Temp" }, c.GetValues("/XD"));
            Assert.Equal(new[] { "*.txt" }, c.FileSpecs);
        }

        [Fact]
        public void Copy_flags_understand_sec_and_copyall()
        {
            Assert.Equal("DAT", RobocopyCommand.Parse("robocopy a b").CopyFlags);
            Assert.Equal("DATS", RobocopyCommand.Parse("robocopy a b /SEC").CopyFlags);
            Assert.Equal("DATSOU", RobocopyCommand.Parse("robocopy a b /COPYALL").CopyFlags);

            var c = RobocopyCommand.Parse("robocopy a b /E /COPYALL /X1");
            c.SetCopyFlags("DATOU");
            Assert.Equal("robocopy a b /E /COPY:DATOU /X1", c.ToString());
        }

        [Fact]
        public void Default_command_has_the_server_copy_profile()
        {
            var c = RobocopyCommand.Parse(RobocopyCommand.DefaultCommand);
            Assert.True(c.HasSwitch("/E"));
            Assert.Equal("DATS", c.CopyFlags);
            Assert.Equal("DAT", c.DirectoryCopyFlags);
            Assert.Equal(2, c.GetIntArgument("/R"));
            Assert.Equal(5, c.GetIntArgument("/W"));
            Assert.Equal("16", c.GetSwitchArgument("/MT"));
            Assert.True(c.HasSwitch("/Z") && c.HasSwitch("/XJ") && c.HasSwitch("/NP"));
            Assert.False(c.HasSwitch("/MIR"));
        }

        [Fact]
        public void Arguments_are_everything_after_the_executable()
        {
            var c = RobocopyCommand.Parse("  robocopy.exe \"C:\\A B\" D:\\X /E  ");
            Assert.True(c.HasExecutable);
            Assert.Equal("\"C:\\A B\" D:\\X /E", c.Arguments);
            Assert.False(RobocopyCommand.Parse("xcopy a b").HasExecutable);
        }
    }

    public class FolderExclusionTests
    {
        private static readonly string[] Listed = { "Accounting", "Archive", "Finance", "Temp" };

        [Fact]
        public void Owns_only_immediate_children_that_are_listed()
        {
            var c = RobocopyCommand.Parse("robocopy \\\\OLD\\Data \\\\NEW\\Data /E /XD $RECYCLE.BIN \\\\OLD\\Data\\Gone \\\\OLD\\Data\\Temp\\Sub");
            FolderExclusions.SetExcludedChildren(c, "\\\\OLD\\Data", Listed, new[] { "Archive", "Temp" });

            Assert.Equal(
                "robocopy \\\\OLD\\Data \\\\NEW\\Data /E /XD $RECYCLE.BIN \\\\OLD\\Data\\Gone \\\\OLD\\Data\\Temp\\Sub \\\\OLD\\Data\\Archive \\\\OLD\\Data\\Temp",
                c.ToString());

            var excluded = FolderExclusions.GetExcludedChildren(c, "\\\\OLD\\Data\\");
            Assert.True(excluded.SetEquals(new[] { "Archive", "Temp", "Gone" }));

            FolderExclusions.SetExcludedChildren(c, "\\\\OLD\\Data", Listed, new string[0]);
            Assert.Equal("robocopy \\\\OLD\\Data \\\\NEW\\Data /E /XD $RECYCLE.BIN \\\\OLD\\Data\\Gone \\\\OLD\\Data\\Temp\\Sub", c.ToString());
        }

        [Fact]
        public void Creates_and_removes_its_own_group()
        {
            var c = RobocopyCommand.Parse("robocopy \"C:\\My Data\" D:\\X /E /CUSTOM");
            FolderExclusions.SetExcludedChildren(c, "C:\\My Data", Listed, new[] { "Temp" });
            Assert.Equal("robocopy \"C:\\My Data\" D:\\X /E /CUSTOM /XD \"C:\\My Data\\Temp\"", c.ToString());

            FolderExclusions.SetExcludedChildren(c, "C:\\My Data", Listed, new string[0]);
            Assert.Equal("robocopy \"C:\\My Data\" D:\\X /E /CUSTOM", c.ToString());
        }

        [Fact]
        public void Drive_root_children()
        {
            Assert.Equal("Temp", FolderExclusions.GetChildName("C:\\Temp", "C:\\"));
            Assert.Equal("C:\\Temp", FolderExclusions.MakeChildPath("C:\\", "Temp"));
            Assert.Null(FolderExclusions.GetChildName("C:\\Temp\\Sub", "C:\\"));
            Assert.Null(FolderExclusions.GetChildName("Temp", "C:\\"));
        }
    }
}
