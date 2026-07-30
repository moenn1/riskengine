using TradingRisk.Application.Abstractions;
using TradingRisk.Application.Common;
using TradingRisk.Domain.Portfolios;

namespace TradingRisk.Application.Portfolios;

/// <summary>
/// Transport-neutral use-case input. Nullable members force the handler/domain to
/// validate callers that do not pass through ASP.NET Core model validation.
/// </summary>
public sealed record CreatePortfolioCommand(
    string? Name,
    string? BaseCurrency,
    IReadOnlyCollection<CreatePositionInput>? Positions);

public sealed record CreatePositionInput(
    string? InstrumentId,
    decimal Quantity,
    decimal Price);

/// <summary>
/// A use-case handler: it coordinates domain creation and persistence without knowing HTTP or
/// the database technology.
/// </summary>
public sealed class CreatePortfolioHandler(IPortfolioRepository repository)
{
    public async Task<PortfolioDto> HandleAsync(
        CreatePortfolioCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Positions is null)
        {
            throw new RequestValidationException("Positions are required.");
        }

        // LINQ maps boundary input to validated domain values; ToArray materializes
        // before aggregate creation so failures happen inside this use case.
        var positions = command.Positions
            .Select(position => Position.Create(
                position.InstrumentId ?? string.Empty,
                position.Quantity,
                position.Price))
            .ToArray();

        var portfolio = Portfolio.Create(
            command.Name,
            command.BaseCurrency,
            positions);

        // The handler depends on the Application-owned port, never on a database type.
        await repository.AddAsync(portfolio, cancellationToken);

        return PortfolioDto.FromDomain(portfolio);
    }
}
