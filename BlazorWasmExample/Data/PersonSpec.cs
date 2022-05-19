using Ardalis.Specification;

namespace BlazorWasmExample.Data
{
    public class PersonSpec : Specification<Person>, ISingleResultSpecification
    {
        public PersonSpec(int Id)
        {
            Query.Where(x => x.Id == Id)
                .Include(x => x.Things);
        }
    }

    public class AllPeopleSpec : Specification<Person>, ISingleResultSpecification
    {
        public AllPeopleSpec()
        {
            Query.Include(x => x.Things);
        }
    }
}
