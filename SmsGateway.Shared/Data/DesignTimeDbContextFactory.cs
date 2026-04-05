using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SmsGateway.Shared.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
    
  
    var connectionString = "Host=localhost;Database=smsc;Username=postgres;Password=123456";
    
    optionsBuilder.UseNpgsql(connectionString);

    return new ApplicationDbContext(optionsBuilder.Options);
    }
}