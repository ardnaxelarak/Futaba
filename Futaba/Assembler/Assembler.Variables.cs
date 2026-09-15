namespace Futaba;

unsafe partial class Assembler {
	private Dictionary<string, Variable> InitialVariables = [];
	private SpannedLookup<Variable> VariablesTable = new(50);

	private void DeclareVariable() {
		const string NoCompound = "Cannot perform compound assignment on a variable that does not yet exist.";
		reading++;

		if (*reading is InternalVariableToken) {
			Error("Internal variables are read-only");
			AbortCommand();
			return;
		}

		var vnameRead = FastRead.ReadAlphaNumeric(ref reading, fileEnd);

		if (vnameRead.Length < 1) {
			Error("Variable missing name.");
			AbortCommand();
			return;
		}

		string varName = vnameRead;

		FastRead.SkipInlineWhitespace(ref reading, fileEnd);

		char assignmentType = *reading;

		ref var theVar = ref VariablesTable.GetRefOrDefault(varName);

		switch (assignmentType) {
			case '=':
				reading++;
				break;

			case '+':
			case '/':
			case '-':
			case '*':
			case '?':
				if (reading[1] is '=') {
					reading += 2;
					break;
				}

				goto default;

			default:
				UnexpectedCharacter();
				AbortCommand();
				return;
		}

		FastRead.SkipInlineWhitespace(ref reading, fileEnd);

		if (!ReadRestOfCommand(out OperandString theRest)) {
			Error("Missing operand for assignment");
			return;
		}

		if (theVar is not null && theVar.VarType is VariableType.String && assignmentType is '+') {
			theVar.Append(theRest);
		} else if (theRest is ['"', .. OperandString operand, '"']) {
			string opcon = ExplodeStringContents(operand);

			switch (assignmentType) {
				case '=':
					if (theVar is null) {
						theVar = new(varName, opcon);
					} else {
						theVar.Contents = opcon;
					}
					break;

				case '+' when theVar is null:
					Error(NoCompound);
					break;

				case '+':
					theVar.Append(opcon);
					break;

				case '?':
					theVar ??= new(varName, opcon);
					break;

				default:
					Error("Invalid assignment type");
					break;
			}
		} else {
			IExpressionReturn oper = ParseOperand(theRest);

			if (oper.Resolved) {
				switch (assignmentType) {
					case '=':
						if (theVar is null) {
							theVar = new(varName, oper.Value);
						} else {
							theVar.Value = oper.Value;
						}
						break;

					case '?':
						theVar ??= new(varName, oper.Value);
						break;

					case '+' or '-' or '*' or '/' when theVar is null:
						Error(NoCompound);
						break;

					case '+':
						theVar.Value += oper.Value;
						break;

					case '-':
						theVar.Value -= oper.Value;
						break;

					case '*':
						theVar.Value *= oper.Value;
						break;

					case '/':
						theVar.Value /= oper.Value;
						break;

					default:
						Error("Invalid assignment type.");
						break;
				}
			} else {
				// create if it doesn't exist to help reduce errors
				theVar ??= new(varName, 0M);
				Error_BadResolve(oper, "Unable to resolve value before assignment.");
			}
		}
	}


	private bool TryReadVariableName(ref char* start, char* end, out CharSpan name) {
		if (!FastRead.TryReadIdentifier(ref start, end, out name)) {
			Error("Missing variable name");
			return false;
		} else {
			return true;
		}
	}

	private bool TryGetVariable(CharSpan name, [MaybeNullWhen(false)] out Variable variable) {
		return VariablesTable.TryGetValue(name, out variable);
	}

//	private Variable GetOrCreateVariable(CharSpan name) {
//		ref var getVar = ref VariablesTable.GetRefOrDefault(name);
//
//		getVar ??= new Variable(new string(name), 0);
//
//		return getVar;
//	}
//
//	private Variable GetOrCreateVariable(string name) {
//		ref var getVar = ref VariablesTable.GetRefOrDefault(name);
//
//		getVar ??= new Variable(name, 0);
//
//		return getVar;
//	}


	/// <summary>
	/// Clears the initial variables dictionary.
	/// </summary>
	/// <inheritdoc cref="ThrowIfAssembling" path="//remarks|//exception"/>
	public void ClearInitialVariables() {
		ThrowIfAssembling();
		InitialVariables.Clear();
	}


	/// <summary>
	/// Creates a new variable with the specified value.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="value"></param>
	/// <inheritdoc cref="ThrowIfAssembling" path="//remarks|//exception"/>
	public void CreateExternalVariable(string name, string value) {
		ThrowIfAssembling();

		InitialVariables[name] = new(name, value);
	}

	/// <inheritdoc cref="CreateExternalVariable(string, string)"/>
	public void CreateExternalVariable(string name, int value) {
		ThrowIfAssembling();

		InitialVariables[name] = new(name, value);
	}

	/// <returns><c>0</c> if invalid</returns>
	internal decimal GetInternalVariable(CharSpan name) {
		if (name.Length < 10) {
			Span<char> tspan = stackalloc char[name.Length];
			name.FastLower(tspan);

			switch (tspan) {
				case "pc": /*            */ return PC;
				case "site": /*          */ return Provenance;
				case "here": /*          */ return Provenance;
				case "offset": /*        */ return Offset;
				case "rand": /*          */ return RNG.NextUInt64();
				case "pi": /*            */ return 3.14159265358979323846264M;
				case "e": /*             */ return 2.71828182845904523536028M;
				case "rad": /*           */ return 0.01745329251994329576923M;
				case "name": /*          */ return 0x03FF;
				case "line": /*          */ return SourceLineNumber;
				case "version": /*       */ return FutabaApp.VersionDecimal;
			}
		}

		Error("Invalid internal variable name");

		return MathHelpers.NaN;
	}

	private string ExplodeStringContents(char* strStart, char* strEnd) {
		return ExplodeStringContents(CharSpanHelpers.CreateSpan(strStart, strEnd));
	}

	private char GetEscapedCharacter(char c) {
		switch (c) {
			case 'n':
				return '\n';

			case 't':
				return '\t';

			case '"':
			case '{':
			case '}':
			case '!':
			case CharDelim:
			case EscapeChar:
				return c;

			default:
				Warning($"Unrecognized escape sequence: \\{c}");
				return c;
		}
	}


	private string ExplodeStringContents(CharSpan str) {
		StringBuilder ret = new();

		char c;

		int sublen = str.Length;

		for (int i = 0; i < sublen; i++) {
			c = str[i];

			if (c is EscapeChar) {
				if (++i < sublen) {
					ret.Append(GetEscapedCharacter(str[i]));
				}
			} else if (c is '!') {
				if (++i == sublen) {
					ret.Append('!');
				} else {
					char dd = str[i];
					if (dd.IsIdentifierCharacter) {
						CharSpan varname = ReadIdentifier(str);

						if (TryGetVariable(varname, out var def)) {
							switch (def.VarType) {
								case VariableType.String:
									ret.Append(def.Contents);
									break;

								case VariableType.Value:
									ret.Append(def.Contents);
									break;
							}
						} else {
							Error($"Variable !{varname} not found.");
						}
						// check for internal variables
					} else if (dd is ':') {
						if (++i == sublen) {
							ret.Append("!:");
						} else {
							dd = str[i];
							if (dd.IsIdentifierCharacter) {
								CharSpan varname = ReadIdentifier(str);
								ret.Append(GetInternalVariable(varname));
							} else {
								ret.Append("!:");
								ret.Append(dd);
							}
						}
					} else {
						ret.Append('!');
						ret.Append(dd);
					}
				}
			} else {
				ret.Append(c);
			}


			/******************************************/

			CharSpan ReadIdentifier(CharSpan subsub) {
				int nomstart = i;
				int adv = nomstart;

				// first char is definitely an 
				while (++adv < sublen && subsub[adv].IsIdentifierCharacter) ;

				i = adv;
				return subsub[nomstart..adv];
			}
		}

		return ret.ToString();
	}
}

