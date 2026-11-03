using AdvancedSID.Core;
using Xunit;

namespace AdvancedSID.Core.Tests;

public class FolderNameParserTests
{
    [Theory]
    [InlineData("S-1-5-21-839522115-583907252-2147053123-448107_Lynne.Todd", "S-1-5-21-839522115-583907252-2147053123-448107", "Lynne.Todd")]
    [InlineData("Lynne.Todd_S-1-5-21-839522115-583907252-2147053123-448107", "S-1-5-21-839522115-583907252-2147053123-448107", "Lynne.Todd")]
    [InlineData("UVHD-S-1-5-21-1-2-3-1001.vhdx", "S-1-5-21-1-2-3-1001", "vhdx")]
    [InlineData("s-1-5-21-1-2-3-500_admin-user", "S-1-5-21-1-2-3-500", "admin-user")]
    public void Parses_sid_and_user(string folder, string sid, string user)
    {
        var (parsedSid, parsedUser) = FolderNameParser.Parse(folder);
        Assert.Equal(sid, parsedSid);
        Assert.Equal(user, parsedUser);
    }

    [Fact]
    public void Folder_without_sid_keeps_name()
    {
        var (sid, user) = FolderNameParser.Parse("Templates");
        Assert.Null(sid);
        Assert.Equal("Templates", user);
    }
}
