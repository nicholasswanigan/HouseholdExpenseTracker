using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HouseholdExpenses.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseSplit> ExpenseSplits => Set<ExpenseSplit>();
    public DbSet<Settlement> Settlements => Set<Settlement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ApplicationUser>()
            .Property(u => u.DisplayName).HasMaxLength(60);

        b.Entity<Household>(e =>
        {
            e.Property(h => h.Name).HasMaxLength(80).IsRequired();
            e.Property(h => h.InviteCode).HasMaxLength(32).IsRequired();
            e.HasIndex(h => h.InviteCode).IsUnique();
        });

        b.Entity<HouseholdMember>(e =>
        {
            e.HasKey(m => new { m.HouseholdId, m.UserId });
            e.Property(m => m.Role).HasConversion<string>().HasMaxLength(16);
            e.HasOne(m => m.Household).WithMany(h => h.Members).HasForeignKey(m => m.HouseholdId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany(u => u.Memberships).HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Expense>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.SplitType).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Household).WithMany(h => h.Expenses).HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
            // Restrict: members who paid for expenses can't be silently deleted out from under the ledger.
            e.HasOne(x => x.Payer).WithMany().HasForeignKey(x => x.PayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.HouseholdId, x.Date });
        });

        b.Entity<ExpenseSplit>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.Expense).WithMany(x => x.Splits).HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ExpenseId, x.UserId }).IsUnique();
        });

        b.Entity<Settlement>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasOne(x => x.Household).WithMany(h => h.Settlements).HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.FromUser).WithMany().HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToUser).WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.HouseholdId, x.Date });
        });
    }
}
