using System.Reflection;
using Bank.Modular.Contracts.Ledger;
using Bank.Modular.Ledger;
using Bank.Modular.Payees;
using Bank.Modular.Statements;
using Microsoft.EntityFrameworkCore;

namespace Bank.Modular.Tests;

/// <summary>
/// B5 stage 1: the rules that keep the modules apart, checked on every build. The compiler
/// already stops a module using another's internal types; these catch what it can't see.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Type[] Modules = [typeof(LedgerModule), typeof(PayeesModule), typeof(StatementsModule)];

    private static string NameOf(Assembly assembly) => assembly.GetName().Name!;

    // #region boundary-tests
    [Fact]
    public void Modules_reference_each_other_only_through_the_contracts()
    {
        var moduleAssemblies = Modules.Select(m => NameOf(m.Assembly)).ToHashSet();
        foreach (var module in Modules)
        {
            var references = module.Assembly.GetReferencedAssemblies().Select(r => r.Name!);
            Assert.DoesNotContain(references, moduleAssemblies.Contains);
        }
    }

    [Fact]
    public void Each_module_makes_public_only_the_class_that_switches_it_on()
    {
        foreach (var module in Modules)
            Assert.Equal([module], module.Assembly.GetExportedTypes());
    }

    [Fact]
    public void Each_module_maps_only_tables_in_its_own_schema()
    {
        foreach (var module in Modules)
        {
            var schema = (string)module.GetField("Schema")!.GetValue(null)!;
            foreach (var context in DbContextsIn(module.Assembly))
            {
                using (context)
                {
                    Assert.All(context.Model.GetEntityTypes(), entity => Assert.Equal(schema, entity.GetSchema()));
                }
            }
        }
    }
    // #endregion boundary-tests

    [Fact]
    public void The_contracts_depend_on_no_module()
    {
        var moduleAssemblies = Modules.Select(m => NameOf(m.Assembly)).ToHashSet();
        var references = typeof(ILedger).Assembly.GetReferencedAssemblies().Select(r => r.Name!);
        Assert.DoesNotContain(references, moduleAssemblies.Contains);
    }

    [Fact]
    public void Every_change_to_a_modules_tables_has_a_migration()
    {
        foreach (var module in Modules)
            foreach (var context in DbContextsIn(module.Assembly))
                using (context)
                {
                    Assert.False(context.Database.HasPendingModelChanges(), $"{context.GetType().Name} has changes without a migration.");
                }
    }

    [Fact]
    public void Every_module_with_tables_is_checked()
    {
        // Guards the schema test: if it found no DbContext, it would pass without checking anything.
        Assert.NotEmpty(DbContextsIn(typeof(LedgerModule).Assembly));
        Assert.NotEmpty(DbContextsIn(typeof(PayeesModule).Assembly));
        Assert.NotEmpty(DbContextsIn(typeof(StatementsModule).Assembly));
    }

    /// <summary>Builds each of an assembly's DbContexts, without a database, to read its model.</summary>
    private static List<DbContext> DbContextsIn(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t =>
            {
                var options = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(t))!;
                options.UseNpgsql("Host=unused").UseSnakeCaseNamingConvention();
                return (DbContext)Activator.CreateInstance(t, options.Options)!;
            })
            .ToList();
}
