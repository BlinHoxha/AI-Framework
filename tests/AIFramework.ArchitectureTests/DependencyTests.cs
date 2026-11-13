using System.Reflection;
namespace AIFramework.ArchitectureTests;
public sealed class DependencyTests
{
    [Theory]
    [InlineData("AIFramework.Core")]
    [InlineData("AIFramework.Contracts")]
    [InlineData("AIFramework.Application")]
    [InlineData("AIFramework.Local")]
    [InlineData("AIFramework.Azure")]
    [InlineData("AIFramework.Aws")]
    [InlineData("AIFramework.Ollama")]
    public void Libraries_have_no_general_framework_or_product_dependency(string name)
    {
        var references = Assembly.Load(name).GetReferencedAssemblies().Select(r => r.Name!).ToArray();
        Assert.DoesNotContain(references, r => r.StartsWith("Framework.") || r.StartsWith("MixPilot") || r.StartsWith("BusinessSuite") || r.StartsWith("AIFramework.SampleApi"));
        if (name is "AIFramework.Core" or "AIFramework.Contracts" or "AIFramework.Application" or "AIFramework.Local")
            Assert.DoesNotContain(references, r => r.StartsWith("Azure.") || r.StartsWith("AWSSDK.") || r.StartsWith("Microsoft.EntityFrameworkCore") || r.StartsWith("Microsoft.AspNetCore"));
        if (name == "AIFramework.Azure") Assert.DoesNotContain("AIFramework.Aws", references);
        if (name == "AIFramework.Aws") Assert.DoesNotContain("AIFramework.Azure", references);
        if (name == "AIFramework.Ollama") Assert.DoesNotContain(references, r =>
            r.StartsWith("Azure.") || r.StartsWith("AWSSDK.") || r.StartsWith("Microsoft.AspNetCore") ||
            r.StartsWith("Microsoft.EntityFrameworkCore") || r is "AIFramework.Azure" or "AIFramework.Aws" or "AIFramework.Application" or "AIFramework.Local");
        if (name is "AIFramework.Azure" or "AIFramework.Aws") Assert.DoesNotContain("AIFramework.Ollama", references);
    }
}
