namespace BlazorWasmExample.Data
{
    public class Person
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public List<Thing> Things { get; set; } = new List<Thing>();
    }
}
