using Npgsql;

namespace Api.Services;

public class IngestService
{
    private readonly NpgsqlDataSource _dataSource;

    public IngestService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }
}
