namespace BrowserHost.Common
{
	public class RenderProcessArgs
	{
		public int ParentPid;
		public string CefAssemblyDir;
		public string DalamudAssemblyDir;

		public string Serialise()
		{
			return TinyJson.JSONWriter.ToJson(this);
		}

		public static RenderProcessArgs Deserialise(string serialisedArgs)
		{
			return TinyJson.JSONParser.FromJson<RenderProcessArgs>(serialisedArgs);
		}
	}
}
