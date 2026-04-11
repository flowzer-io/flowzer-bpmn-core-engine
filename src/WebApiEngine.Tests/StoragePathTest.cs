using FilesystemStorageSystem;
using FluentAssertions;

namespace WebApiEngine.Tests;

[NonParallelizable]
public class StoragePathTest
{
    [Test]
    public void GetBasePath_ShouldUseConfiguredStorageRoot_WhenEnvironmentVariableIsSet()
    {
        var originalValue = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
        var tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-storage-root-test", Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, tempRoot);

            var storage = new Storage();
            var resolvedPath = storage.GetBasePath("FileStorage/Forms");

            resolvedPath.Should().StartWith(Path.GetFullPath(tempRoot));
            Directory.Exists(resolvedPath).Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, originalValue);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
