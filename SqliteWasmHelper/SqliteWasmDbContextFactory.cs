// <copyright file="SqliteWasmDbContextFactory.cs" company="Jeremy Likness">
// Copyright (c) Jeremy Likness. All rights reserved.
// </copyright>

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SqliteWasmHelper
{
    /// <summary>
    /// Defers sending back the context until the database is restored, and backs up on
    /// succcessful saves.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DbContext"/>.</typeparam>
    public class SqliteWasmDbContextFactory<TContext> : ISqliteWasmDbContextFactory<TContext>
        where TContext : DbContext
    {
        private static readonly IDictionary<Type, string> FileNames = new Dictionary<Type, string>();

        private readonly IDbContextFactory<TContext> dbContextFactory;
        private readonly IBrowserCache cache;
        private readonly ISqliteSwap swap;
        private Task<int>? startupTask = null;
        private int lastStatus = -2;
        private bool init = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteWasmDbContextFactory{TContext}"/> class.
        /// </summary>
        /// <param name="dbContextFactory">The EF Core-provided factory.</param>
        /// <param name="cache">The <see cref="BrowserCache"/> helper.</param>
        /// <param name="swap">Service to perform the backup.</param>
        public SqliteWasmDbContextFactory(
            IDbContextFactory<TContext> dbContextFactory,
            IBrowserCache cache,
            ISqliteSwap swap)
        {
            this.cache = cache;
            this.dbContextFactory = dbContextFactory;
            this.swap = swap;
            startupTask = RestoreAsync();
        }

        /// <summary>
        /// Gets an easy reference to filename. Only accessed after initialization.
        /// </summary>
        private static string Filename => FileNames[typeof(TContext)];

        /// <summary>
        /// Gets an easy reference to the backup file.
        /// </summary>
        private static string BackupFile => $"{SqliteWasmDbContextFactory<TContext>.Filename}_bak";

        /// <summary>
        /// Mainly for testing purposes.
        /// </summary>
        public static void Reset() => FileNames.Clear();

        /// <summary>
        /// Gets the cached filenames for each <see cref="DbContext"/> type.
        /// </summary>
        /// <returns>The name or null.</returns>
        public static string? GetFilenameForType() =>
            FileNames.ContainsKey(typeof(TContext)) ? FileNames[typeof(TContext)] : null;

        /// <summary>
        /// Create a new <see cref="DbContext"/>.
        /// </summary>
        /// <returns>The new instance.</returns>
        public async Task<TContext> CreateDbContextAsync()
        {
            // first time should wait for restore to happen
            await CheckForStartupTaskAsync();

            // grab the context
            var ctx = await dbContextFactory.CreateDbContextAsync();

            if (!init)
            {
                // first time, it should be created
                await ctx.Database.EnsureCreatedAsync();
                init = true;
            }

            // hook into saved changes
            // By using lambda's as a wrapper we avoid the need to dispose the listener...
            ctx.SavingChanges += (o, e) => Ctx_SavingChanges(ctx, e);
            ctx.SaveChangesFailed += (o, e) => Ctx_SaveChangesFailed(ctx, e);
            ctx.SavedChanges += (o, e) => Ctx_SavedChanges(ctx, e);

            ctx.ChangeTracker.StateChanged += (o, e) => ChangeTracker_StateChanged(ctx, e);

            return ctx;
        }


        public TContext CreateDbContext()
        {
            // first time should wait for restore to happen
            CheckForStartupTaskAsync();

            // grab the context
            var ctx = dbContextFactory.CreateDbContext();

            if (!init)
            {
                // first time, it should be created
                ctx.Database.EnsureCreated();
                init = true;
            }

            // hook into saved changes
            ctx.SavedChanges += (o, e) => Ctx_SavedChanges(ctx, e);

            return ctx;
        }

        private void DoSwap(string source, string target) =>
            swap.DoSwap(source, target);

        /// <summary>
        /// Method called once to reverse engineer filename from connection string.
        /// </summary>
        /// <returns>The filename.</returns>
        private string GetFilename()
        {
            using var ctx = dbContextFactory.CreateDbContext();
            var filename = "filenotfound.db";
            var type = ctx.GetType();
            if (FileNames.ContainsKey(type))
            {
                return FileNames[type];
            }

            var cs = ctx.Database.GetConnectionString();

            if (cs != null)
            {
                var file = cs.Split(';').Select(s => s.Split('='))
                    .Select(split => new
                    {
                        key = split[0].ToLowerInvariant(),
                        value = split[1],
                    })
                    .Where(kv => kv.key.Contains("data source") ||
                    kv.key.Contains("datasource") ||
                    kv.key.Contains("filename"))
                    .Select(kv => kv.value)
                    .FirstOrDefault();
                if (file != null)
                {
                    filename = file;
                }
            }

            FileNames.Add(type, filename);
            return filename;
        }

        private async Task CheckForStartupTaskAsync()
        {
            if (startupTask != null)
            {
                lastStatus = await startupTask;
            }

            if (startupTask != null)
            {
                startupTask.Dispose();
                startupTask = null;
            }
        }

        private async void Ctx_SavingChanges(object? ctx, SavingChangesEventArgs e)
        {
            var changes = (ctx as TContext).ChangeTracker
                .Entries()
                .Where(x => x.State != EntityState.Unchanged &&
                            x.State != EntityState.Detached)
                .ToList();

            Console.WriteLine("Saving...");
            changes.ForEach(x => Console.WriteLine(x.DebugView.LongView));
        }
        
        private void ChangeTracker_StateChanged(object? ctx, EntityStateChangedEventArgs e)
        {
            var changed = e.Entry;
            Console.WriteLine("state changes on...");
            changed.CurrentValues.ToString();
        }

        private async void Ctx_SaveChangesFailed(object? ctx, SaveChangesFailedEventArgs e)
        {
            var changes = (ctx as TContext).ChangeTracker
                .Entries()
                .Where(x => x.State != EntityState.Unchanged &&
                            x.State != EntityState.Detached)
                .ToList();
            Console.WriteLine("Saving FAILED...");
            changes.ForEach(x => Console.WriteLine(x.DebugView.LongView));
        }

        private async void Ctx_SavedChanges(object? ctx, SavedChangesEventArgs e)
        {
            await (ctx as TContext).Database.CloseConnectionAsync();
            await CheckForStartupTaskAsync();
            
            if (e.EntitiesSavedCount > 0)
            {
                // unique to avoid conflicts. Is deleted after cahcing.
                var backupName =
                    $"{SqliteWasmDbContextFactory<TContext>.BackupFile}-{Guid.NewGuid().ToString().Split('-')[0]}";
                DoSwap(SqliteWasmDbContextFactory<TContext>.Filename, backupName);
                lastStatus = await cache.SyncDbWithCacheAsync(backupName);
            }
            
            Console.WriteLine($"Saved {e.EntitiesSavedCount} entities OK...");
        }

        private async Task<int> RestoreAsync()
        {
            var filename = $"{GetFilename()}_bak";
            lastStatus = await cache.SyncDbWithCacheAsync(filename);
            if (lastStatus == 0)
            {
                DoSwap(filename, FileNames[typeof(TContext)]);
            }

            return lastStatus;
        }
    }
}
