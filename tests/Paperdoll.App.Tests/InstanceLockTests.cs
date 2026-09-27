namespace Paperdoll.App.Tests;

public class InstanceLockTests
{
    [Fact]
    public void A_second_Paperdoll_is_refused()
    {
        var data = Directory.CreateTempSubdirectory("paperdoll-lock-").FullName;
        var before = Environment.GetEnvironmentVariable("PAPERDOLL_DATA");
        Environment.SetEnvironmentVariable("PAPERDOLL_DATA", data);
        try
        {
            Assert.True(InstanceLock.TryAcquire());
            // What a second copy of Paperdoll would try.
            Assert.Throws<IOException>(() => new FileStream(Path.Combine(data, "paperdoll.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PAPERDOLL_DATA", before);
        }
    }
}
