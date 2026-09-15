namespace Futaba;

internal sealed unsafe class MacroCall {
	private readonly string macroName;
	private readonly SourceLine DeclarationSite;

	internal int Arity { get; }

	internal bool IsFunction { get; init; }

	internal List<string> ArgumentNames { get; }

	private readonly string Body;

	private readonly string macroid;
	private int invocations = 0;

	// using reserved unicode characters to allow fast replacement
	// (they need replacing anyways to preempt false positives)
	private const char SpecialMarker = '\uF201';
	private const string SpecialID = "\uF200";

	internal MacroCall(string name, List<string> argumentNames, StringBuilder body, int macronum, SourceLine decSite) {
		macroName = name;

		Arity = argumentNames.Count;

		ArgumentNames = argumentNames;

		for (int i = 0; i < Arity; i++) {
			body.Replace($"{{{ArgumentNames[i]}}}", $"{(char) (SpecialMarker + i)}");
		}

		body.Replace("{?}", SpecialID);

		Body = body.ToString();

		macroid = $"M{macronum:X5}";
		DeclarationSite = decSite;

	}

	internal bool TryExplode(List<OperandString> args, string uniqueId, [NotNullWhen(true)] out string? invoker, [MaybeNullWhen(true)] out string? errors) {
		invocations++;

		int arglen = args.Count;

		if (arglen != Arity) {
			invoker = null;
			errors = arglen < Arity
				? "Not enough arguments in macro call"
				: "Too many arguments in macro call";
			return false;
		}

		StringBuilder ret = new(Body);

		// replace these temporarily to prevent recursive replacements

		for (int i = 0; i < Arity; i++) {
			CharSpan repv = (string) args[i];

			if (repv is ['"', .. var contents, '"']) {
				repv = contents;
			}
			
			ret.Replace($"{(char) (SpecialMarker + i)}", repv);
		}

		ret.Replace(SpecialID, uniqueId);

		invoker = ret.ToString();

		errors = null;
		return true;
	}

	private string GetUniqueCallName() => $"{macroid}{invocations:X5}";




	internal bool TryGetInvoker(SourceLine callSite, List<OperandString> args, [MaybeNullWhen(false)] out SourceObject invoker, out string? errors) {
		string uniqueId = GetUniqueCallName();

		if (!TryExplode(args, uniqueId, out var theBody, out errors)) {
			invoker = null;
			return false;
		}

		invoker = new MacroInvocation(this, theBody, uniqueId, callSite);

		errors = null;

		return true;
	}

	private sealed class MacroInvocation : SourceObject {
		internal override bool IsMacro => true;
		private readonly string macroIdName;

		public override FileInfo? FileInfo => CallSite.Source.FileInfo;
		private readonly SourceLine CallSite;
		private readonly MacroCall MacroClass;
		internal MacroInvocation(MacroCall macroClass, string body, string name, SourceLine callSite) {
			int bufferLen = body.Length;

			AllocBlock = Allocate(bufferLen);

			SourceStart = AllocBlock + BufferPadding;

			body.AsSpan().CopyTo(new(SourceStart, body.Length));

			SourceEnd = SourceStart + body.Length;

			ApplyPadding();
			macroIdName = name;

			CallSite = callSite;
			MacroClass = macroClass;
		}

		public override string ObjectName => macroIdName;

		public override string ToString() {
			return $"{CallSite.Source}: [Macro \"{MacroClass.macroName}\" (@{MacroClass.DeclarationSite})] ";
		}
	}




}
