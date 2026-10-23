using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WorkingService;

// Database context (like your old TorrenTEntities)
public class DBcontext : DbContext
{
    //private readonly string _connectionString;

    //public DBcontext(string connectionString)
    //{
    //    _connectionString = connectionString;
    //}

    //public DbSet<MdtalityTestAPIDetails> MdtalityTestAPIDetails { get; set; }

    //protected override void OnConfiguring(DbContextOptionsBuilder options)
    //{
    //    options.UseSqlServer(_connectionString);
    //}
}

// One row of the table. Change names/types here if your table is different.
//[Table("MdtalityTestAPIDetails", Schema = "dbo")]
//public class MdtalityTestAPIDetails
//{
//    [Key]
//    public long Id { get; set; }

//    public long? OrgId { get; set; }                  // used to find the rows to update

//    public string? ProcessStatus { get; set; }       // "Y" = success, "E" = error
//    public string? ResponseMessage { get; set; }             // success / failure message
//    public string? ClientResponse { get; set; }      // response text from client
//    public DateTime? ModifiedAT { get; set; } // date time of the response
//}

