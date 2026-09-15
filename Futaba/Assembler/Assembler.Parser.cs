namespace Futaba;

unsafe partial class Assembler {
	private const int MaxChain = 512;


	/// <summary>
	/// Returns whether or not this instance is busy with assembly.
	/// </summary>
	public bool Busy { get; private set; } = false;

	private SourcePointers? previousState = null;

	private SourceObject CurrentSourceObject = SourceFile.Empty;
	private SourceLine CurrentSourceLine => new(CurrentSourceObject, SourceLineNumber, lastCommandStart);

	private char* reading = default;
	private char* lastCommandStart = default;
	private char* fileEnd = default;
	private IfLink? ifstack = null;
	private int SourceLineNumber = 0;

	/// <summary>
	/// Things that should be done both before and after assembly
	/// both to reduce allocations and have a consistent outside state
	/// </summary>
	private protected virtual void RequiredInitAndCleanUp() {
		CurrentEncoder = SnesEncoder.ASCII;

		previousState = null;
		ifstack = null;
		currentSegment = null;

		Array.Fill(LabelNameHierarchy, string.Empty);
		Array.Clear(QuayHierarchy);

		ClearPools();
	}

	private void TryAssembly() {
		try {
			Busy = true;

			MessageOut.Reset();
			ErrorOut.Reset();

			// reset the assembler's state
			ErrorCount = 0;
			ErrorLine = null;

			if (!TryGetSource(EntryPoint.FullName, out var entryObj)) {
				return;
			}

			ShutUpAboutAddresses = false;
			ShutUpAboutSegments = false;

			RNG.Reseed(RNGSeed);

			ResetRomBuffer();

			// Initial pass of header sets properties that the user can overwrite
			if (AutoPopulateHeader) {
				InitializeHeader();
			}

			arch = SnesArchitecture.WDC65816;
			BankGuardMask = BankGuardMask_Half;

			BreakpointsAre = BreakpointsControl.Off;
			InProvenance = false;
			NextAllocBlockStart = -1;

			// reset symbols
			SymbolsTable.Clear();
			SymbolsTable.CopyEntries(initialsymbols);

			Array.Clear(pluslabels);
			Array.Clear(minuslabels);

			VariablesTable.Clear();

			foreach ((string k, Variable v) in InitialVariables) {
				VariablesTable.Add(k, v.Clone());
			}

			RequiredInitAndCleanUp();

			// reset source state
			previousState = null;
			TransferSourceControl(entryObj);

			StartAssembly();
			ResolveAllRequests();

			if (AutoPopulateHeader) {
				FinalizeHeader();
			} else if (CalculateChecksum) {
				RecalculateChecksum();
			}
		} catch {
			if (CurrentSourceObject == SourceFile.Empty) {
				ErrorLine = "Unable to determine line.";
			} else {
				var srcLine = CurrentSourceLine;
				if (!srcLine.IsNull) {
					ErrorLine = $"{srcLine}";
				}
			}

			throw;
		} finally {
			RequiredInitAndCleanUp();

			CurrentSourceObject = SourceFile.Empty;

			foreach (var (fileName, fileObj) in LoadedFiles) {
				if (fileObj.WasUsed) {
					fileObj.WasUsed = false;
				} else {
					fileObj.Dispose();
					LoadedFiles.Remove(fileName);
				}
			}

			foreach (var (fileName, fileObj) in LoadedBinaries) {
				if (fileObj.WasUsed) {
					fileObj.WasUsed = false;
				} else {
					fileObj.Dispose();
					LoadedBinaries.Remove(fileName);
				}
			}

			foreach (var srcObj in LoadedSources) {
				srcObj?.Dispose();
			}

			LoadedSources.Clear();
			MacrosTable.Clear();
			UserFunctionsTable.Clear();
			Encoders.Clear();

			SimpleRequests.Clear();
			FillRequests.Clear();
			JumpRequests.Clear();
			BranchRequests.Clear();

			Segments.Clear();

			PCStack.Clear();
			PVStack.Clear();

			Busy = false;
		}
	}


	private void StartAssembly() {
		do {
			while (reading < fileEnd) {
				// find the next command
				char c = *reading;

				// TODO benchmark if this is still a good idea (probably irrelevant)
				// skip over spaces
				// these are so common that we'll handle them outside of the switch for efficiency

				if (c.IsSkippableSpace) {
					reading++;
					// very fast whitespace skipping
					//char* rd = reading; // put in register
					//while ((++rd)->IsSkippableSpace) ;
					//
					//reading = rd;
					continue;
				} else if (c is NewLine) {
					reading++;
					SourceLineNumber++;
					continue;
				}

				lastCommandStart = reading;

				// everything else is some sort of interesting character!
				switch (c) {
					// skip over comments
					case CommentChar:
						FastRead.ReadUntilLineEnd(ref reading, fileEnd);
						continue;

					case ':':
						if (FastRead.ColonIsSeparator(reading)) {
							reading += 2;
						} else {
							UnexpectedCharacter();
							AbortCommand();
						}
						continue;

					case '|':
						GenerateAddressMarker();
						continue;

					case '!':
						DeclareVariable();
						continue;

					case '#':
						GenerateExtrinsicLabel();
						continue;

					case '%':
						InvokeMacro();
						continue;

					case '+':
					case '-':
						GenerateRelativeLabel(c);
						continue;

					case '^':
						GenerateBuoy();
						continue;

					case '.':
						GenerateSublabel();
						continue;

					case '`':
						GenerateBreakpoint();
						continue;

					case '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9':
					case '/':
					case '"':
					case '$':
					case '&':
					case '\'':
					case '(':
					case ')':
					case '*':
					case ',':
					case '<':
					case '=':
					case '>':
					case '\\':
					case '[':
					case ']':
					case '~':
					case '@':
						UnexpectedCharacter();
						AbortCommand();
						continue;

					// undocumented because I honestly hate this
					case '{':
					case '}':
						reading++;
						continue;

					// symbols / labels / mnemonics / directives
					case	'A' or 'B' or 'C' or 'D' or 'E' or 'F' or 'G' or 'H' or 'I' or 'J' or 'K' or 'L' or 'M' or
							'N' or 'O' or 'P' or 'Q' or 'R' or 'S' or 'T' or 'U' or 'V' or 'W' or 'X' or 'Y' or 'Z' or
							'a' or 'b' or 'c' or 'd' or 'e' or 'f' or 'g' or 'h' or 'i' or 'j' or 'k' or 'l' or 'm' or
							'n' or 'o' or 'p' or 'q' or 'r' or 's' or 't' or 'u' or 'v' or 'w' or 'x' or 'y' or 'z' or '_':
						// no need to do an identifier check, since the first character is absolutely valid
						OperandString symOpDir = FastRead.ReadAlphaNumeric(ref reading, fileEnd);

						// check for a colon
						if (*reading is ':') {
							reading++;

							if (DemandSpaceAfterColon()) {
								Label addLabel = new(symOpDir, Provenance, PC, Offset);

								AddOrReplaceSymbol(addLabel);
								UpdateHierarchy(addLabel.Name, 0);

								if (pooling) {
									Error($"Use '{ENDPOOL}' before declaring a new hierarchy.");
									ClearPools();
								}
							}

							continue;
						}

						char* peeking = reading;

						FastRead.SkipInlineWhitespace(ref peeking, fileEnd);

						if (*peeking is '=') {
							reading = peeking + 1;
							CreateAssignedSymbol(symOpDir);
							continue;
						}

						// try treating it as a data statement
						if (symOpDir.Length is 2 && c is 'd' or 'D') {
							// we could technically do a special check for the SPC instruction di here
							// but no one should ever use that, so who cares if it's a little slower?
							switch (symOpDir[1]) {
								case 'b' or 'B': DoDataItems(1); continue;
								case 'w' or 'W': DoDataItems(2); continue;
								case 'l' or 'L': DoDataItems(3); continue;
								case 'd' or 'D': DoDataItems(4); continue;
							}
						}

						// test for it being an instruction
						// if it is, assemble it
						if (arch is SnesArchitecture.WDC65816) {
							OpWdc opco = TestFor65816(symOpDir);

							if (opco is not OpWdc.NotGood) {
								Assemble65816(opco);
								continue;
							}
						} else if (arch is SnesArchitecture.SPC700) {
							OpSPC opco = TestForSPC700(symOpDir);

							if (opco is not OpSPC.NotGood) {
								AssembleSPC700(opco);
								continue;
							}
						} else if (arch is SnesArchitecture.SuperFX) {
							OpSFX opco = TestForSuperFX(symOpDir);

							if (opco is not OpSFX.NotGood) {
								AssembleSuperFX(opco);
								continue;
							}
						}

						HandleDirectives(symOpDir);
						continue;

					default:
						UnexpectedCharacter();
						AbortCommand();
						continue;

				} // end switch
			} // end source while
		} while (PopSourceControl()); // end stack while
	}

	private void HandleDirectives(OperandString directive) {
		// Assume everything else is a directive
		int dlen = directive.Length;

		if (dlen <= LongestDirectiveLength) {
			scoped Span<char> dirword = stackalloc char[dlen];
			directive.FastLower(dirword);

			switch (dirword) {
				case PILE:
					Directive_PILE();
					return;

				case PLOP:
					Directive_PLOP();
					return;

				case PILING:
					Directive_PILING();
					return;

				case DOCK:
					Directive_DOCK();
					return;

				case UNDOCK:
					currentSegment = null;
					return;

				case POOL:
					Directive_POOL();
					return;

				case ENDPOOL:
					if (!pooling) {
						Warning("Not currently pooling.");
					}

					ClearPools();
					FinishCommand();
					return;

				case INCSRC:
					Directive_INCSRC();
					return;

				case INCBIN:
					Directive_INCBIN();
					return;

				case ORG:
					if (DemandSpaceWithError() && TryReadRestAsExpression(ORG, SymbolContext.RomAddress, out IExpressionReturn orgpc)) {
						TryMovePC(orgpc);
					}

					return;

				case SITE:
					if (DemandSpaceWithError() && DemandRestOfCommand(out OperandString site)) {
						if (site.OnlyCharIs(SPLAT)) {
							InProvenance = false;
						} else {
							IExpressionReturn siteval = ParseOperand(site, SymbolContext.Provenance);

							if (siteval.Resolved) {
								Provenance = siteval.ValueInt32;
							} else {
								Error_BadResolve(siteval, MsgInfo.MustBeResolvedNow(SITE));
							}
						}
					}

					return;

				case SAFEORG:
					if (DemandSpaceWithError() && DemandRestOfCommand(out OperandString safeOrgArg)) {
						IExpressionReturn safewarn = ParseOperand(safeOrgArg, SymbolContext.Provenance);
						IExpressionReturn safeorg = ParseOperand(safeOrgArg, SymbolContext.RomAddress);

						if (safeorg.Resolved && safewarn.Resolved) {
							WarnCounter(safewarn.ValueInt32, PC);
							TryMovePC(safeorg);
						} else {
							Error_BadResolve(safeorg, $"argument to {SAFEORG} must be immediately resolvable");
						}
					}

					return;

				case WARNPC:
					if (DemandSpaceWithError() && TryReadRestAsExpression(WARNPC, SymbolContext.Provenance, out IExpressionReturn warnpc)) {
						WarnCounter(warnpc.ValueInt32, PC);
					}

					return;

				case WARNSITE:
					if (DemandSpaceWithError() && TryReadRestAsExpression(WARNPC, SymbolContext.Provenance, out IExpressionReturn warnsite)) {
						WarnCounter(warnsite.ValueInt32, Provenance);
					}

					return;

				case SKIP:
					if (DemandSpaceWithError() && TryReadRestAsExpression(SKIP, SymbolContext.Provenance, out IExpressionReturn skipSize)) {
						Skip(skipSize.ValueInt32);
					}

					return;

				case SKIPTO:
					if (DemandSpaceWithError() && TryReadRestAsExpression(SKIPTO, SymbolContext.RomAddress, out IExpressionReturn skiptoExpr)) {
						int skiptoValue = skiptoExpr.ValueInt32;

						if (skiptoValue > 0) {
							int skipAmount = skiptoValue - Provenance;

							if (skipAmount >= 0) {
								Skip(skipAmount);
							} else {
								Error($"Target of {SKIPTO} is behind current provenance.");
							}
						} else {
							Error(MsgInfo.PositiveArgument);
						}
					}
					return;

				case ALIGN:
					if (DemandSpaceWithError() && TryReadRestAsExpression(ALIGN, SymbolContext.RomAddress, out IExpressionReturn alignSize)) {
						int alignValue = alignSize.ValueInt32;

						if (alignValue > 0) {
							int skipAmount = PC % alignValue;

							if (skipAmount is not 0) {
								Skip(alignValue - skipAmount);
							}
						} else {
							Error(MsgInfo.PositiveArgument);
						}
					}
					return;

				case ARRANGE:
					if (DemandSpaceWithError() && TryReadRestAsExpression(ARRANGE, SymbolContext.Provenance, out IExpressionReturn arrangeSize)) {
						int arrangeValue = arrangeSize.ValueInt32;

						if (arrangeValue > 0) {
							int skipAmount = Provenance % arrangeValue;

							if (skipAmount is not 0) {
								Skip(arrangeValue - skipAmount);
							}
						} else {
							Error(MsgInfo.PositiveArgument);
						}
					}
					return;

				case PUSHPC:
					PushPC();
					FinishCommand();
					return;

				case PULLPC:
					PullPC();
					FinishCommand();
					return;

				case PUSHSITE:
					PushProvenance();
					FinishCommand();
					return;

				case PULLSITE:
					PullProvenance();
					FinishCommand();
					return;

				case BANKGUARD:
					Directive_BANKGUARD();
					return;

				case REBANK:
					Directive_REBANK();
					return;

				case ALLOCATE:
					Directive_ALLOCATE();
					return;

				case FILL:
					Directive_FILL();
					return;

				case BREAKPOINTS:
					Directive_BREAKPOINTS();
					return;

				case LANG:
				case ARCH:
					Directive_LANG();
					return;

				case MMC:
					Directive_MMC();
					return;

				case RAW:
					Directive_RAW();
					return;

				case BYTE:
				case WORD:
				case LONG:
				case DOUBLE:
				case SIZE:
				case COUNT:
				case RANDOM:
				case ENDALLOCATE:
					Error(MsgInfo.UnexKeyword, directive.ToString());
					AbortCommand();
					return;

				default:
					if (HandleMoreDirectives(dirword)) {
						return; // exit if directive was handled
					}

					break; // otherwise fall through to the error
			}
		}

		Error("Unrecognized text", $"\"{directive}\"");
		AbortCommand();
	}


	// These directives can be used in multiple block types,
	// so they're split out for shared access.
	private bool HandleMoreDirectives(CharSpan directive) {
		switch (directive) {
			case PRINT:
				if (TryGetPrintContents(out var contents)) {
					if (contents is not null) {
						MessageOut.WriteLine(contents);
					} else {
						MessageOut.WriteLine();
					}
				}
				return true;

			case PRINTF:
				Directive_PRINTF();
				return true;

			case WARN:
				if (TryGetPrintContents(out contents)) {
					Console.ForegroundColor = ConsoleColor.DarkYellow;

					ErrorOut.Write(CurrentSourceLine);
					ErrorOut.Write(WarningText);
					ErrorOut.WriteLine(contents ?? "Unspecified warning");

					Console.ResetColor();
				}
				return true;

			case ERROR:
				if (TryGetPrintContents(out contents)) {
					Console.ForegroundColor = ConsoleColor.Red;

					ErrorOut.Write(CurrentSourceLine);
					ErrorOut.Write(ErrorText);
					ErrorOut.WriteLine(contents ?? "Unspecified error");

					Console.ResetColor();

					IncrementErrors();
				}
				return true;

			case MACRO:
				Directive_MACRO();
				return true;

			case FUNCTION:
				Directive_FUNCTION();
				return true;

			case IF:
				Directive_IF();
				return true;

			case ELSE:
				Directive_ELSE();
				return true;

			case ENDIF:
				Directive_ENDIF();
				return true;

			case ENCODER:
				Directive_ENCODER();
				return true;
		}

		return false;
	}






	private void TransferSourceControl<T>(T source) where T : SourceObject {
		if ((previousState?.Depth ?? 0) >= MaxChain) {
			string MaxObj = $"The maximum number ({MaxChain}) of nested or chained assembly objects has been hit.";
			Error(MaxObj);
			throw new InvalidOperationException(MaxObj);
		}

		if (typeof(T) != typeof(SourceFile)) {
			LoadedSources.Add(source);
		}

		if (CurrentSourceObject != SourceFile.Empty) {
			previousState = new(CurrentSourceObject, reading, SourceLineNumber, ifstack, previousState);
		}

		CurrentSourceObject = source;
		fileEnd = source.SourceEnd;
		reading = source.SourceStart;
		lastCommandStart = reading;
		SourceLineNumber = 1;
		ifstack = null;
	}

	private bool PopSourceControl() {
		// check the parent for where to resume or if we should end
		if (previousState is SourcePointers sp) {

			// TODO maybe have this for a verbose mode
#if DEBUG
			//if (CurrentSourceObject is SourceFile) {
			//	Console.WriteLine($"Finished {CurrentSourceObject}");
			//}
#endif

			previousState = sp.Parent;
			(CurrentSourceObject, reading, SourceLineNumber, ifstack) = sp;
			fileEnd = CurrentSourceObject.SourceEnd;

			return true;
		} else {
			return false;
		}
	}

	private class IfLink(IfLink? parent) {
		internal readonly IfLink? Parent = parent;
		internal readonly int Depth = parent?.Depth ?? 1;
		internal bool HitElse = false;
	}

	private sealed class SourcePointers(SourceObject source, char* current, int line, IfLink? ifstack, SourcePointers? parent) {
		internal readonly SourcePointers? Parent = parent;
		internal readonly SourceObject Source = source;
		internal readonly char* Current = current;
		internal readonly int Line = line;
		internal readonly IfLink? IfStack = ifstack;
		internal readonly int Depth = (parent?.Depth + 1) ?? 0;

		public void Deconstruct(out SourceObject _source, out char* _current, out int _line, out IfLink? _ifstack) {
			_current = Current;
			_line = Line;
			_source = Source;
			_ifstack = IfStack;
		}
	}

}
