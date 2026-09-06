using NUnit.Framework;

public sealed class SteamRoomRegressionTests
{
    [Test]
    public void RoomNumberRejectsWhitespaceLettersUnicodeDigitsAndWrongLength()
    {
        Assert.That(SteamRoomService.IsValidRoomCode("123456"), Is.True);
        foreach (string code in new[] { null, "", "12345", "1234567", "12345a", " 12345", "１２３４５６", "١٢٣٤٥٦" })
            Assert.That(SteamRoomService.IsValidRoomCode(code), Is.False, code);
    }

    [Test]
    public void SharedTestAppRoomsRequireExactGameProtocolBuildAndRoomNumber()
    {
        const string code = "123456", build = "0.1.0";
        Assert.That(SteamRoomService.MatchesRoom("MessUP", SteamRoomService.ProtocolMarker, build, code, code, build), Is.True);
        Assert.That(SteamRoomService.MatchesRoom("Spacewar", SteamRoomService.ProtocolMarker, build, code, code, build), Is.False);
        Assert.That(SteamRoomService.MatchesRoom("MessUP", "old-protocol", build, code, code, build), Is.False);
        Assert.That(SteamRoomService.MatchesRoom("MessUP", SteamRoomService.ProtocolMarker, "0.0.1", code, code, build), Is.False);
        Assert.That(SteamRoomService.MatchesRoom("MessUP", SteamRoomService.ProtocolMarker, build, "654321", code, build), Is.False);
    }
}
