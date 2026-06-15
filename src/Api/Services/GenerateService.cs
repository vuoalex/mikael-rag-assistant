using Npgsql;

namespace Api.Services;

public class GenerateService
{
    private readonly NpgsqlDataSource _dataSource;

    public GenerateService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }
}
