using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.AppCatalog.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.AppCatalog.Queries.GetGlobalApplicationLogo;

public sealed record GlobalApplicationLogo(byte[] Content, string ContentType);
public sealed record GetGlobalApplicationLogoQuery(Guid Id) : IRequest<Result<GlobalApplicationLogo>>;

public sealed class GetGlobalApplicationLogoQueryHandler(IGlobalApplicationRepository applications)
    : IRequestHandler<GetGlobalApplicationLogoQuery, Result<GlobalApplicationLogo>>
{
    public async Task<Result<GlobalApplicationLogo>> Handle(GetGlobalApplicationLogoQuery request, CancellationToken ct)
    {
        var app = await applications.GetByIdAsync(request.Id, ct);
        if (app?.Logo is not { Length: > 0 })
            return Result<GlobalApplicationLogo>.Failure("Application logo not found.", 404);
        return Result<GlobalApplicationLogo>.Success(new GlobalApplicationLogo(
            app.Logo,
            app.LogoContentType ?? "application/octet-stream"));
    }
}
