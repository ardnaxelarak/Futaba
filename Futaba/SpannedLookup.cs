namespace Futaba;

internal readonly struct SpannedLookup<T> where T : notnull {
	private readonly Dictionary<string, T> dictionary;
	private readonly Dictionary<string, T>.AlternateLookup<CharSpan> altlookup;

	public int Count => dictionary.Count;

	public SpannedLookup() : this(0) { }

	public SpannedLookup(int capacity) {
		dictionary = new(capacity);
		altlookup = dictionary.GetAlternateLookup<CharSpan>();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref T? GetRefOrDefault(string name) {
		return ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, name, out _);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref T? GetRefOrDefault(CharSpan name) {
		return ref CollectionsMarshal.GetValueRefOrAddDefault(altlookup, name, out _);
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ContainsKey(string name) {
		return dictionary.ContainsKey(name);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ContainsKey(CharSpan name) {
		return altlookup.ContainsKey(name);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryGetValue(string name, [MaybeNullWhen(false)] out T item) {
		return dictionary.TryGetValue(name, out item!);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryGetValue(CharSpan name, [MaybeNullWhen(false)] out T item) {
		return altlookup.TryGetValue(name, out item!);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Add(string name, T value) {
		dictionary[name] = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clear() {
		dictionary.Clear();
	}

	public void CopyEntries(Dictionary<string, T> source) {
		foreach ((string k, T v) in source) {
			dictionary.Add(k, v);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Dictionary<string, T>.Enumerator GetEnumerator() {
		return dictionary.GetEnumerator();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Dictionary<string, T> GetCopy() {
		return new(dictionary);
	}



}
