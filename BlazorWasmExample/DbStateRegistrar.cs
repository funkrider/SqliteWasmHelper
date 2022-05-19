using Ardalis.Specification;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using SqliteWasmHelper;

namespace BlazorWasmExample
{
    public class DbStateRegistrar
    {
        private Dictionary<IComponent, string> ComponentDictionary = new Dictionary<IComponent, string>();

        public static void RegisterComponent<TResult, TContext>(
            IComponent cmpt, 
            ISpecification<TResult> spec,
            ISqliteWasmDbContextFactory<TContext> DbCtxFactory,
            IEnumerable<string> qParams,
            string result) where TContext : DbContext
        {

            /*
            We want to effectively run the blow as a function whenever
            1) the query properties change on the component or 
            2) the database changes a value that is currently used by the component.
            
            await using var ctx = await DbCtxFactory.CreateDbContextAsync();
		    person = ctx.Persons.WithSpecification(Query).SingleOrDefault();
             */

            

        }
    }
}
