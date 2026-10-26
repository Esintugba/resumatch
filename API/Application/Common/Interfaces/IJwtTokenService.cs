using Api.Application.Common.Models;
using Api.Entities;

namespace Api.Application.Common.Interfaces;

public interface IJwtTokenService
{
    AccessTokenResult GenerateAccessToken(User user);
}
