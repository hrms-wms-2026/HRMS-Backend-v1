namespace ONEVO.Api.Middleware;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AllowWithoutActiveTrayAttribute : Attribute
{
}
