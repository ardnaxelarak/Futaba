using System.Xml.Linq;

namespace Futaba;

unsafe partial class Assembler {
	private void FinishCommand() {
		if (!AdvanceOverWhitespace()) {
			Error("Extraneous content after expected end of command");
			AbortCommand();
		}
	}

	/// <summary>
	/// returns true if hit end of command
	/// </summary>
	private bool AdvanceOverWhitespace() {
		FastRead.SkipInlineWhitespace(ref reading, fileEnd);

		char c = *reading;

		if (c is CommentChar) {
			FastRead.ReadUntilLineEnd(ref reading, fileEnd);
			return true;
		}

		return AtEndOfCommand();
	}

	private bool AtEndOfCommand() {
		char* rd = reading;

		if (rd >= fileEnd) {
			return true;
		}

		return (*rd is NewLine or CommentChar) || FastRead.TestIfColonAndSeparator(rd);
	}


	/// <summary>
	/// Returns false and aborts if no space
	/// </summary>
	private bool DemandSpaceWithError() {
		if (reading->IsSkippableSpace) {
			FastRead.SkipInlineWhitespace(ref reading, fileEnd);
			return true;
		} else {
			if (AtEndOfCommand()) {
				Error("Unexpected end of command");
			} else {
				UnexpectedCharacter();
				AbortCommand();
			}
			return false;
		}

	}

	private void AbortCommand() {
		char* read = reading;
		char* theend = fileEnd;

		while (read < theend) {
			char c = *read;

			if (c is NewLine or CommentChar) {
				break;
			}

			if (FastRead.TestIfColonAndSeparator(read)) {
				break;
			}

			if (c is '"') {
				// no need to validate the quotes, because the command has been aborted
				_ = FastRead.SkipToMatchingQuote(ref read, theend);
			}

			read++;
		}

		reading = read;
	}

	private bool TestIsQuotedStringAndExplode(CharSpan arg, [NotNullWhen(true)] out string? exploded) {
		if (arg is ['"', .. CharSpan contents, '"']) {
			exploded = ExplodeStringContents(contents);
			return true;
		} else {
			exploded = null;
			return false;
		}
	}

	private bool TryGetQuotedStringExploded(CharSpan arg, [NotNullWhen(true)] out string? exploded) {
		if (arg is ['"', .. CharSpan contents, '"']) {
			exploded = ExplodeStringContents(contents);
			return true;
		} else {
			exploded = null;
			Error("Invalid string");
			return false;
		}
	}


	private SizeToken GetSizeToken() {
		char* read = reading;

		if (*read is '.') {
			read += 2;

			if (read->IsSkippableSpace) {
				read++;
				reading = read;
				char tok = (read - 2)->FastLower();

				switch (tok) {
					case 'b': return SizeToken.B;
					case 'w': return SizeToken.W;
					case 'l': return SizeToken.L;
				}

				Error(MsgInfo.InvalidAddressingMode, details: $"invalid size token: {tok}.");
			} else {
				reading = read;
				UnexpectedCharacter();
			}
		} else if (read->IsNiceBoundary) {
			return SizeToken.Unspecified;
		} else {
			UnexpectedCharacter();
		}

		return SizeToken.Invalid;
	}

	private bool ReadUntilAllocator(out OperandString conts) {
		char* theend = fileEnd;
		char* read = reading;
		char* constart = read;

		for (; read < theend; read++) {
			char c = *read;

			if (c is NewLine or CommentChar) {
				break;
			}

			if (c is '"') {
				FastRead.SkipToMatchingQuote(ref read, theend);
				continue;
			}

			if (c is ':') {
				if (FastRead.ColonIsSeparator(read)) {
					conts = new(constart, read);
					reading = read + 1;
					return false;
				}

				if (read[1] is ':') {
					conts = new(constart, read);
					reading = read + 2;
					return true;
				}
				continue;
			}
		}

		reading = read;
		conts = OperandString.CreateTrimmed(constart, read);
		return false;
	}


	private bool ReadForAllocator(out OperandString postContents) {
		if (!AdvanceOverWhitespace()) {
			if (FastRead.IsAllocator(reading)) {
				reading += 2;
				ReadRestOfCommand(out postContents);
				return true;
			}
		}

		postContents = default;
		return false;
	}

	private bool TryReadRestAsExpression(string directiveName, SymbolContext context, out IExpressionReturn value) {
		if (DemandRestOfCommand(out OperandString skipArg)) {
			value = ParseOperand(skipArg, context);

			if (value.Resolved) {
				return true;
			} else {
				Error_BadResolve(value, MsgInfo.MustBeResolvedNow(directiveName));
			}
		}

		value = MathHelpers.InvalidExpression;
		return false;
	}

	private CharSpan GetNextKeyword() {
		FastRead.SkipInlineWhitespace(ref reading, fileEnd);

		var cspan = FastRead.ReadAlphaNumericSpan(ref reading, fileEnd);

		int len = cspan.Length;

		if (len is < 1) {
			return string.Empty;
		} else if (len > LongestDirectiveLength) {
			return cspan;
		}

		Span<char> dspan = stackalloc char[len];
		cspan.FastLower(dspan);
		return new string(dspan);
	}


	/// <inheritdoc cref="RestOfCommand(out char*, out char*)"/>
	private bool ReadRestOfCommand(out CharSpan operand) {
		if (RestOfCommand(out char* opstart, out char* opend)) {
			operand = CharSpanHelpers.CreateSpan(opstart, opend);
			return true;
		}

		operand = [];
		return false;
	}

	/// <inheritdoc cref="RestOfCommand(out char*, out char*)"/>
	private bool ReadRestOfCommand(out OperandString operand) {
		if (RestOfCommand(out char* opstart, out char* opend)) {
			operand = new(opstart, opend);
			return true;
		}

		operand = default;
		return false;
	}

	private bool DemandRestOfCommand(out OperandString operand) {
		if (ReadRestOfCommand(out operand)) {
			return true;
		} else {
			Error(MsgInfo.MissingDirectiveArgument);
			return false;
		}
	}


	private bool CaptureLine(out CharSpan operand) {
		bool ret = ReadRestOfCommand(out operand);

		if (*reading is NewLine) {
			SourceLineNumber++;
			reading++;
		}

		return ret;
	}


	/// <summary>
	/// returns false if nothing is found. trims ends of string
	/// </summary>
	private bool RestOfCommand(out char* opstart, out char* opend) {
		opstart = reading;
		char* theend = fileEnd;

		char* read = opstart;

		for (; read < theend; read++) {
			char c = *read;

			// For accurate error reporting,
			// line increment needs to be deferred to the caller
			if (c is NewLine) {
				break;
			}

			if (c is CommentChar) {
				reading = read + 1;
				FastRead.ReadUntilLineEnd(ref reading, theend);

				goto FinishUp;
			}

			if (FastRead.TestIfColonAndSeparator(read)) {
				reading = read + 2;
				goto FinishUp;
			}

			if (c is '"') {
				FastRead.SkipToMatchingQuote(ref read, theend);
			}
		}

		reading = read;

		FinishUp:

		opend = read;
		FastRead.TrimWhiteSpace(ref opstart, ref opend);

		return opstart != opend;
	}

	/// <returns>true if theres another comma</returns>
	private bool ReadToNextComma(out OperandString stuff) {
		char* theend = fileEnd;

		FastRead.SkipInlineWhitespace(ref reading, theend);

		char* read = reading;
		char* start = read;

		for (; read < theend; read++) {
			char c = *read;
			if (c is NewLine or CommentChar) {
				break;
			}

			if (c is '"') {
				if (FastRead.SkipToMatchingQuote(ref read, theend)) continue;
				break;
			}

			// skip parens, since those may have commas in them for functions and macros
			if (c is '(') {
				if (FastRead.SkipToMatchingParenthesis(ref read, theend)) continue;
				break;
			}

			if (FastRead.TestIfColonAndSeparator(read)) {
				break;
			}

			if (c is ',') {
				stuff = OperandString.CreateTrimmed(start, read);
				reading = read + 1;
				return true;
			}
		}

		reading = read;

		stuff = OperandString.CreateTrimmed(start, read);
		return false;
	}

	private StringBuilder? CaptureBlock(string startBlock, string endBlock, string blockTypeName) {
		StringBuilder ret = new();

		int blockDepth = 1;

		while (blockDepth > 0) {
			if (reading >= fileEnd) {
				Error($"Unclosed {blockTypeName} block");
				return null;
			}

			if (!CaptureLine(out CharSpan blockLine)) {
				continue;
			}

			if (blockLine.IsEmpty) {
				continue;
			}

			if (IsActualKeyWord(blockLine, endBlock)) {
				if (blockLine.Length > endBlock.Length) {
					Error($"{endBlock} must appear by itself");
				}

				blockDepth--;

				if (blockDepth is 0) {
					break;
				}

			} else if (IsActualKeyWord(blockLine, endBlock)) {
				blockDepth++;
			}

			ret.Append(blockLine).Append(NewLine);
		}

		return ret;

		static bool IsActualKeyWord(CharSpan checkline, string testString) {
			if (checkline.BeginsWithIgnoreSpace(testString)) {
				if (checkline.Length == testString.Length) {
					return true;
				}

				return checkline[testString.Length].IsSkippableSpace;
			}
			return false;
		}
	}





	private bool TryReadAndExplodeStringContents(ref char* stread, char* stend, [NotNullWhen(true)] out string? str) {
		if (FastRead.TryReadOutQuotedString(ref stread, stend, out CharSpan span)) {
			str = ExplodeStringContents(span);
			return true;
		} else {
			str = null;
			return false;
		}
	}

	internal bool TryGetSource(string path, [NotNullWhen(true)] out SourceFile? file) {
		if (TryGetFileName(path, out var fullPath)) {
			ref var fileRef = ref CollectionsMarshal.GetValueRefOrAddDefault(LoadedFiles, fullPath, out _);

			if (fileRef is not null) {
				fileRef.Refresh();
			} else {
				fileRef = new(fullPath);
			}

			if (fileRef.Error is not null) {
				SourceLine eline = CurrentSourceObject == SourceFile.Empty
					? default
					: CurrentSourceLine;

				Error("Problem loading file", fileRef.Error, eline);

				file = null;
				return false;
			}

			fileRef.WasUsed = true;

			file = fileRef;
			return true;
		} else {
			file = null;
			return false;
		}
	}

	internal bool TryGetBinary(string path, [NotNullWhen(true)] out SourceBinary? file) {
		if (TryGetFileName(path, out var fullPath)) {
			ref var fileRef = ref CollectionsMarshal.GetValueRefOrAddDefault(LoadedBinaries, fullPath, out _);

			if (fileRef is not null) {
				fileRef.Refresh();
			} else {
				fileRef = new(fullPath);
			}

			if (fileRef.Error is not null) {
				SourceLine eline = CurrentSourceObject == SourceFile.Empty
					? default
					: CurrentSourceLine;

				Error("Problem loading file", fileRef.Error, eline);

				file = null;
				return false;
			}

			fileRef.WasUsed = true;

			file = fileRef;
			return true;
		} else {
			file = null;
			return false;
		}
	}


	internal bool TryGetFileName(string path, [NotNullWhen(true)] out string? filePath) {
		if (Path.IsPathFullyQualified(path)) {
			filePath = path;
			return true;

		} else if (CurrentSourceObject.FileInfo?.Directory is DirectoryInfo dir) {
			// fully qualify it
			filePath = Path.Join(dir.FullName, path);
			return true;

		} else {
			Error("Unable to find current directory.");
			filePath = null;
			return false;
		}
	}

	private bool ReadOutCommaItemsWithParen([NotNullWhen(true)] out List<OperandString>? args) {
		FastRead.SkipInlineWhitespace(ref reading, fileEnd);

		if (*reading is not '(') {
			args = null;
			Error("Missing opening parenthesis");
			return false;
		}


		args = [];
		char* theend = fileEnd;

		char* read = reading + 1;
		char* start = read;

		FastRead.SkipInlineWhitespace(ref read, theend);

		if (read < theend && *read is ')') {
			read++;
			reading = read;
			return true;
		}


		for (; read < theend; read++) {
			char c = *read;

			if (c is NewLine or CommentChar) {
				break;
			}

			if (c is '"') {
				if (FastRead.SkipToMatchingQuote(ref read, theend)) continue;
				break;
			}

			// skip parens, since those may have commas in them when functions
			if (c is '(') {
				if (FastRead.SkipToMatchingParenthesis(ref read, theend)) continue;
				break;
			}

			if (c is ',') {
				args.Add(OperandString.CreateTrimmed(start, read));
				start = read + 1;
				continue;
			}

			if (c is ')') {
				args.Add(OperandString.CreateTrimmed(start, read));
				read++;
				reading = read;
				return true;
			}

			if (FastRead.TestIfColonAndSeparator(read)) {
				break;
			}
		}

		Error("Mismatched parentheses");
		reading = read;
		args = null;
		return false;
	}

	// TODO add mask functions for AND / OR / XOR
	private bool TryReadStringWithParams(OperandString strArg, [NotNullWhen(true)] out string? strparsed, out SnesEncoder encoding) {
		encoding = CurrentEncoder;

		if (strArg.First is not '"') {
			strparsed = null;
			return false;
		}

		var (read, theend) = strArg;

		if (!FastRead.TryReadOutQuotedString(ref read, theend, out CharSpan contents)) {
			strparsed = null;
			return false;
		}


		// we have a string, so check for the next character
		if (FastRead.SkipInlineWhitespaceWithEndCheck(ref read, theend)) {
			// no next character, so it's a normal string

			strparsed = ExplodeStringContents(contents);
			return true;
		}

		if ((read + 3) >= strArg.End
			 || read[0] is not '/'
			 || read[1] is not '/'
			 || read[2] is not '/') {
			strparsed = null;
			return false;
		}

		strparsed = ExplodeStringContents(contents);

		read += 3;

		if (read == strArg.End) {
			return true;
		}

		bool paddingHappened = false;
		bool casingHappened = false;
		bool encoderHappened = false;

		// check each rule

		StringBuilder bld = new(strparsed);

		Span<char> cmdlbuffer = stackalloc char[10];

		StringSplitter args = new(read, theend);

		bool more;

		do {
			more = args.GetNextChar('/', out var cmdWhole);

			if (cmdWhole == default) {
				Error("Missing command name in string instructions");
				break;
			}

			int i = 0;

			// improve to a search values thing
			while (i < cmdWhole.Length && cmdWhole[i].IsALetter) {
				i++;
			}

			var cmd = cmdWhole[..i];

			if (i is > 0 and < 9) {
				cmd.FastLower(cmdlbuffer);

				var cmdl = cmdlbuffer[..i];

				switch (cmdl) {
					case "upper":
						Recase(char.ToUpper);
						continue;

					case "lower":
						Recase(char.ToLower);
						continue;

					case "pad":
						DoPadding(true);
						continue;

					case "len":
						DoPadding(false);
						continue;

					case "enc":
						if (encoderHappened) {
							Warning("Duplicate encoder command in string");
						}

						if (!HasColonForCommand()) {
							Error("Missing colon in encoder command");
							continue;
						}

						CharSpan encoderName = cmdWhole[i..];

						if (TryGetEncoder(encoderName, out var getEncoder)) {
							encoding = getEncoder;
						} else {
							Error("Encoder does not exist.");
						}
						continue;
				}

				bool HasColonForCommand() {
					return cmdWhole[i++] is ':';
				}

				void DoPadding(bool isPad) {
					if (paddingHappened) {
						Warning("Duplicate resize command in string");
					}

					paddingHappened = true;

					if (!HasColonForCommand()) {
						Error("Missing colon in resize command");
						return;
					}

					char dir = cmdWhole[i];
					int strlen = bld.Length;

					switch (dir) {
						case 'c' or 'C':
							dir = cmdWhole[i + 1];

							bool alignedLeft;
							switch (dir) {
								case 'l' or 'L'
									or '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9': // to account for size being next
									alignedLeft = true;
									break;

								case 'r' or 'R':
									alignedLeft = false;
									break;

								default:
									Warning("Bad padding mode");
									return;
							}

							if (!TryGetLength(out int lenArg)) {
								return;
							}

							// already the correct length
							if (strlen == lenArg) {
								return;
							}

							if (strlen < lenArg) {
								// padding commands dont resize
								if (!isPad) {
									bld.Length = lenArg;
								}

								return;
							}

							// otherwise, it's too long
							if (TryGetPadder(out char padChar)) {
								int diff = lenArg - strlen;

								string padStr = new(padChar, diff / 2);

								
								bld.Insert(0, padStr);
								bld.Append(padStr);

								// if they're even, just add to both ends and we're done
								if ((diff & 1) is 1) {
									if (alignedLeft) {
										bld.Append(padChar);
									} else {
										bld.Insert(0, padChar);
									}
								}

								return;
							} else {
								return;
							}




						case 'l' or 'L' or 'r' or 'R':
							if (!TryGetLength(out lenArg)) {
								return;
							}

							// already the correct length
							if (strlen == lenArg) {
								return;
							}

							if (strlen < lenArg) {
								// padding commands dont resize
								if (!isPad) {
									bld.Length = lenArg;
								}

								return;
							}

							// otherwise, it's too long
							if (TryGetPadder(out padChar)) {
								string padStr = new(padChar, lenArg - strlen);

								if (dir is 'r' or 'R') {
									bld.Insert(0, padStr);
								} else {
									bld.Append(padStr);
								}

								return;
							} else {
								return;
							}

						default:
							Warning("Bad padding mode");
							break;
					}

					bool TryGetPadder(out char padChar) {
						char qcheck = cmdWhole[i++];
						if (qcheck is '"') {
							qcheck = cmdWhole[i++];

							if (qcheck is '"') {
								padChar = BadChar;
								Warning("Bad padding char");
								return false;
							} else if (qcheck is EscapeChar) {
								padChar = GetEscapedCharacter(cmdWhole[i++]);
							} else {
								padChar = cmdWhole[i++];
							}

							qcheck = cmdWhole[i++];

							if (qcheck is not '"') {
								Warning("Bad padding char");
								return false;
							}

							return true;
						}

						padChar = ' '; // default to space

						return qcheck is BadChar; // bad char means the end, so we can let it slide
					}

					bool TryGetLength(out int len) {
						if (cmdWhole.TryReadInlineDecimalInteger(ref i, out len)) {
							return true;
						} else {
							Error("Invalid length argument in resize command");
							return false;
						}
					}

				}

				void Recase(Func<char, char> transform) {
					if (casingHappened) {
						Warning("Duplicate casing command in string");
					}

					for (int i = 0, len = bld.Length; i < len; i++) {
						bld[i] = transform(bld[i]);
					}

					casingHappened = true;
				}
			}

			Error($"Missing or invalid string argument: {cmd}");


		} while (more);

		return true;
	}
}
