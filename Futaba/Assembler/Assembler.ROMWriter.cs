namespace Futaba;

unsafe partial class Assembler {
	private const int RomBufferPadding = 128;
	private const int TimeSize = sizeof(uint);

	private protected int PC = 0;
	private int Site = 0;

	private byte* RomBuffer;
	private uint* TimeBuffer;
	private byte* PcPointer;
	private int AllocatedRomLength = 0;

	private uint WriteTime = 0;

	internal int Offset;


	private bool InProvenance = false;

	internal int Provenance {
		get => InProvenance ? Site : PC;
		set {
			InProvenance = true;
			Site = value;
		}
	}

	private bool ShutUpAboutAddresses = false;

	private Stack<int> PCStack = new(64);
	private Stack<long> PVStack = new(64);

	private void PushPC() {
		PCStack.Push(PC);
	}

	private void PullPC() {
		if (PCStack.TryPop(out int newpc)) {
			PC = newpc;
			Offset = AddressToOffset(PC);
			PcPointer = RomBuffer + Offset;
		} else {
			Error("No program counter to pop from stack.");
		}
	}

	private void PushProvenance() {
		if (InProvenance) {
			PVStack.Push(Site);
		} else {
			PVStack.Push(long.MinValue);
		}
	}

	private void PullProvenance() {
		if (PVStack.TryPop(out long newpv)) {
			if (newpv is long.MinValue) {
				InProvenance = false;
			} else {
				Site = (int) newpv;
				InProvenance = true;
			}
		} else {
			Error("No address to pop from stack.");
		}
	}

	private void Skip(int steps) {
		if (TryAdvanceProgramCounter(steps)) {
			AdvanceProgramCounter(steps);
		}
	}

	/// <summary>
	/// Placeholders to help keep things aligned in ROM
	/// </summary>
	private void PlaceholderOp(byte opcode, int operandSize) {
		if (TryAdvanceProgramCounter(operandSize + 1)) {
			PcPointer[0] = opcode;
			new Span<byte>(PcPointer + 1, operandSize).Clear();
			AdvanceProgramCounter(operandSize);
		}
	}

	/// <inheritdoc cref="PlaceholderOp(byte, int)"/>
	private void PlaceholderOp(byte opcode) {
		if (TryAdvanceProgramCounter(1)) {
			PcPointer[0] = opcode;
			TrackAndStep1();
		}
	}


	private void WarningIfWeirdLocalJump(int addr, int dest) {
		if (!JumpTargetMakesSense(addr, dest)) {
			Warning("Possibly erroneous absolute jump; use .w to suppress this message.");
		}
	}

	private void WarnCounter(int guarded, int newLocation) {
		if (newLocation > guarded) {
			Error($"Current location (${newLocation:X6}) has passed guarded location ${guarded:X6}.");
		}
	}

	private static void PerformBlockFill(byte* ptr, int blocksize, int wordsize, int val) {
		if (blocksize < 1) {
			return;
		}

		// timing handled by fill command
		int chunkSize;
		int left;

		switch (wordsize) {
			case 1:
				SnesHelpers.FillBlockB(ptr, (byte) val, blocksize);
				break;

			case 2:
				chunkSize = blocksize >> 1;

				SnesHelpers.FillBlockW(ptr, (ushort) val, chunkSize);

				if ((blocksize & 1) is 1) {
					ptr[blocksize - 1] = (byte) val;
				}

				break;

			case 3:
				(chunkSize, left) = Math.DivRem(blocksize, 3);

				ushort val16 = SnesHelpers.FixBigEndian((ushort) val);

				for (int i = chunkSize; i > 0; i--, ptr += 3) {
					*(ushort*) ptr = val16;
					ptr[2] = (byte) (val >> 16);
				}

				if (left is 1) {
					ptr[0] = (byte) val;

				} else if (left is 2) {
					*(ushort*) ptr = val16;

				}

				break;

			case 4:
				chunkSize = blocksize >> 2;

				SnesHelpers.FillBlockD(ptr, val, chunkSize);

				left = blocksize & 3;

				if (left is 1) {
					ptr[blocksize - 1] = (byte) val;

				} else if (left is 2) {
					SnesHelpers.WriteW(ptr + blocksize - 2, val);

				} else if (left is 3) {
					SnesHelpers.WriteL(ptr + blocksize - 3, val);
				}

				break;


			default:
				for (int start = 0; start < wordsize; start++) {
					byte fillval = (byte) val;
					val >>= 8;

					for (int j = start; j < blocksize; j += wordsize) {
						ptr[j] = fillval;
					}
				}
				break;
		}
	}

	private void FullfillBlockFillRequest(int offset, int blocksize, int wordsize, int val, uint time) {
		if (blocksize < 1) {
			return;
		}

		byte* writePtr = RomBuffer + offset; // put these into registers
		uint* timePtr = TimeBuffer + offset;

		if (wordsize is 1) {
			byte addVal = (byte) val;
			for (int i = 0; i < blocksize; i++, writePtr++, timePtr++) {
				if (*timePtr <= time) {
					*writePtr = addVal;
					*timePtr = time;
				}
			}

			return;
		} else {
			for (int start = 0; start < wordsize; start++) {
				byte fillval = (byte) val;
				val >>= 8;

				for (int j = start; j < blocksize; j += wordsize) {
					var h = timePtr[j];
					if (timePtr[j] <= time) {
						writePtr[j] = fillval;
						timePtr[j] = time;
					}
				}
			}
		}
	}

	

	private void WriteData(IExpressionReturn val, int size) {
		if (TryAdvanceProgramCounter(size)) {
			if (val.NeedsRequest) {
				CreateSimpleRequest(val, Offset, size);
			} else {
				switch (size) {
					case 1:
						PcPointer[0] = (byte) val.ValueInt32;
						break;

					case 2:
						SnesHelpers.WriteW(PcPointer, val.ValueInt32);
						break;

					case 3:
						SnesHelpers.WriteL(PcPointer, val.ValueInt32);
						break;

					case 4:
						SnesHelpers.WriteD(PcPointer, val.ValueInt32);
						break;
				}
			}

			TrackAndStep(size);
		}
	}

	private void TrackAndStep(int step) {
		new Span<uint>(TimeBuffer + Offset, step).Fill(WriteTime++);
		AdvanceProgramCounter(step);
	}


	private void WriteData(int offset, IExpressionReturn val, int size) {
		if (TestOffsetBounds(offset + size)) {
			if (val.NeedsRequest) {
				CreateSimpleRequest(val, offset, size);
			} else {
				byte* wrTemp = RomBuffer + offset;

				switch (size) {
					case 1:
						wrTemp[0] = (byte) val.ValueInt32;
						break;

					case 2:
						SnesHelpers.WriteW(wrTemp, val.ValueInt32);
						break;

					case 3:
						SnesHelpers.WriteL(wrTemp, val.ValueInt32);
						break;

					case 4:
						SnesHelpers.WriteD(wrTemp, val.ValueInt32);
						break;
				}
			}

			ReserveRange(offset, size);
		}
	}

	private void WriteData(int offset, int value, int size) {
		if (TestOffsetBounds(offset + size)) {
			byte* wrTemp = RomBuffer + offset;

			switch (size) {
				case 1:
					wrTemp[0] = (byte) value;
					break;

				case 2:
					SnesHelpers.WriteW(wrTemp, value);
					break;

				case 3:
					SnesHelpers.WriteL(wrTemp, value);
					break;

				case 4:
					SnesHelpers.WriteD(wrTemp, value);
					break;
			}

			ReserveRange(offset, size);
		}
	}

	// TODO try to use vector128.shuffle?
	private void InsertEncodedString(int offset, int wordsize, string text, SnesEncoder encoder) {
		int length = text.Length;

		byte* bufferB = RomBuffer + offset;
		var enc = encoder.EncoderFunction;

		switch (wordsize) {
			case 1:
				foreach (char c in text) {
					*bufferB = (byte) enc(c);
					bufferB++;
				}
				break;

			case 2:
				if (BitConverter.IsLittleEndian && encoder == SnesEncoder.Unicode) {
					text.AsSpan().CopyTo(new Span<char>(bufferB, length));
				} else {
					foreach (char c in text) {
						SnesHelpers.WriteW(bufferB, (int) enc(c));
						bufferB += 2;
					}
				}

				break;

			case 3:
				foreach (char c in text) {
					SnesHelpers.WriteL(bufferB, (int) enc(c));
					bufferB += 3;
				}
				break;

			case 4:
				foreach (char c in text) {
					SnesHelpers.WriteD(bufferB, (int) enc(c));
					bufferB += 4;
				}

				break;
		}

		ReserveRange(offset, length * wordsize);
	}

	private void ReserveRange(int offset, int count) {
		new Span<uint>(TimeBuffer + offset, count).Fill(WriteTime++);
	}

	// The routines in this region are roughly organized such that
	// the TrackAndStep routines appear after the ones that use them
	// just to maybe influence cleaner compilation wrt cache
	#region writes


	private void WriteOpImplied(IExpressionReturn opcode) {
		if (TryAdvanceProgramCounter(1)) {
			if (opcode.NeedsRequest) {
				CreateSimpleRequest(opcode, Offset, 1);
			} else {
				PcPointer[0] = (byte) opcode.ValueInt32;
			}

			TrackAndStep1();
		}
	}

	void AssembleImplied(OperandString argstr, SizeToken argsize, byte opcode) {
		if (argsize is not SizeToken.Unspecified) {
			TokensOnImplied();
		}

		if (!argstr.IsEmpty) {
			InvalidAddressingMode();
		}

		WriteOpImplied(opcode);
	}

	private void WriteOpImplied(byte opcode) {
		if (TryAdvanceProgramCounter(1)) {
			PcPointer[0] = opcode;
			TrackAndStep1();
		}
	}

	private void TrackAndStep1() {
		TimeBuffer[Offset] = WriteTime++;
		AdvanceProgramCounter(1);
	}



	private void AssembleBranch(OperandString argstr, byte opcode) {
		int len = argstr.Length;

		if (len is 0) {
			MissingOperand();
			PlaceholderOp(opcode, 1);
		} else {
			char c = argstr.GetUnchecked(0);

			// check for special characters
			if (len is 1) {
				if (c is BROP) {
					WriteOpBrop(opcode);
					return;
				} else if (c is JNOP) {
					WriteOpB(opcode, 0x00);
					return;
				} else if (c is BTYS) {
					WriteOpB(opcode, 0xFE);
					return;
				}
			}

			if (c is BranchLiteral) {
				WriteOpB(opcode, ParseOperandSliced(argstr, 1));
			} else {
				WriteOpRel8(opcode, ParseOperand(argstr));
			}
		}
	}



	private void WriteOpRel8(byte opcode, IExpressionReturn val) {
		if (TryAdvanceProgramCounter(2)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			if (val.NeedsRequest) {
				BranchRequests.Add(new BranchRequest(val, Offset + 1, Provenance + 2, WriteTime, CurrentSourceLine));
			} else {
				var (good, distance) = SnesHelpers.TestGetBranchDistance(Provenance + 2, val.ValueInt32);

				if (good) {
					wr[1] = (byte) distance;
				} else {
					wr[1] = 0; // placeholder
					Error(SnesHelpers.BranchDistanceError(distance, 1));
				}
			}

			TrackAndStep2();
		}
	}

	private void WriteOpBrop(byte opcode) {
		if (TryAdvanceProgramCounter(2)) {
			PcPointer[0] = opcode;
			// Operand write intentionally skipped
			TrackAndStep2();
		}
	}

	private void WriteOpB(byte opcode, byte val) {
		if (TryAdvanceProgramCounter(2)) {
			byte* wr = PcPointer;
			wr[0] = opcode;
			wr[1] = val;
			TrackAndStep2();
		}
	}

	private void WriteOpB(byte opcode, IExpressionReturn val) {
		if (TryAdvanceProgramCounter(2)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			if (val.NeedsRequest) {
				CreateSimpleRequest(val, Offset + 1, 1);
			} else {
				wr[1] = (byte) val.ValueInt32;
			}

			TrackAndStep2();
		}
	}

	private void TrackAndStep2() {
		uint* mod = TimeBuffer + Offset;
		uint wt = WriteTime++;
		mod[0] = wt;
		mod[1] = wt;
		AdvanceProgramCounter(2);
	}


	private void WriteOpBB(byte opcode, IExpressionReturn val1, IExpressionReturn val2) {
		if (TryAdvanceProgramCounter(3)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			if (val1.NeedsRequest) {
				CreateSimpleRequest(val1, Offset + 1, 1);
			} else {
				wr[1] = (byte) val1.ValueInt32;
			}

			if (val2.NeedsRequest) {
				CreateSimpleRequest(val2, Offset + 2, 1);
			} else {
				wr[2] = (byte) val2.ValueInt32;
			}

			TrackAndStep3();
		}
	}

	private void WriteOpBB(byte opcode, IExpressionReturn val1, byte val2) {
		if (TryAdvanceProgramCounter(3)) {
			byte* wr = PcPointer;
			wr[0] = opcode;
			wr[2] = val2;

			if (val1.NeedsRequest) {
				CreateSimpleRequest(val1, Offset + 1, 1);
			} else {
				wr[1] = (byte) val1.ValueInt32;
			}

			TrackAndStep3();
		}
	}

	private void WriteOpBB(byte opcode, byte val1, IExpressionReturn val2) {
		if (TryAdvanceProgramCounter(3)) {
			byte* wr = PcPointer;
			wr[0] = opcode;
			wr[1] = val1;

			if (val2.NeedsRequest) {
				CreateSimpleRequest(val2, Offset + 2, 1);
			} else {
				wr[2] = (byte) val2.ValueInt32;
			}

			TrackAndStep3();
		}
	}

	private void WriteOpBRel(byte opcode, IExpressionReturn val1, IExpressionReturn val2) {
		if (TryAdvanceProgramCounter(3)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			if (val1.NeedsRequest) {
				CreateSimpleRequest(val1, Offset + 1, 1);
			} else {
				wr[1] = (byte) val1.ValueInt32;
			}

			if (val2.NeedsRequest) {
				BranchRequests.Add(new BranchRequest(val2, Offset + 2, Provenance + 3, WriteTime, CurrentSourceLine));
			} else {
				var (good, distance) = SnesHelpers.TestGetBranchDistance(Provenance + 3, val2.ValueInt32);

				if (good) {
					wr[2] = (byte) distance;
				} else {
					wr[2] = 0; // placeholder
					Error(SnesHelpers.BranchDistanceError(distance, 1));
				}
			}

			TrackAndStep3();
		}
	}

	private void WriteOpBBrop(byte opcode, IExpressionReturn val1) {
		if (TryAdvanceProgramCounter(3)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			if (val1.NeedsRequest) {
				CreateSimpleRequest(val1, Offset + 1, 1);
			} else {
				wr[1] = (byte) val1.ValueInt32;
			}

			// Operand write intentionally skipped

			TrackAndStep3();
		}
	}

	/// <summary>
	/// JMP (abs)   JML [abs]
	/// </summary>
	private void WriteOpWBank00(byte opcode, IExpressionReturn val) {
		if (TryAdvanceProgramCounter(3)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			if (val.NeedsRequest) {
				if (MissingTokenSeverity >= MissingTokenSeverity.AbsolutePointers) {
					JumpRequests.Add(new DirectBankPointerRequest(val, Offset + 1, Provenance, WriteTime, CurrentSourceLine));
				} else {
					CreateSimpleRequest(val, Offset + 1, 2);
				}
			} else {
				int vval = val.ValueInt32 & SnesHelpers.AbsoluteMask;

				if (MissingTokenSeverity >= MissingTokenSeverity.AbsolutePointers) {
					WarningIfWeirdLocalJump(vval, Provenance);
				}

				SnesHelpers.WriteW(wr + 1, val.ValueInt32);
			}

			TrackAndStep3();
		}
	}

	private void WriteOpRel16(byte opcode, IExpressionReturn val) {
		if (TryAdvanceProgramCounter(3)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			if (val.NeedsRequest) {
				BranchRequests.Add(new BranchRequestLong(val, Offset + 1, Provenance + 3, WriteTime, CurrentSourceLine));
			} else {
				var (good, distance) = SnesHelpers.TestGetBranchDistanceLong(Provenance + 3, val.ValueInt32);

				if (good) {
					SnesHelpers.WriteW(wr + 1, distance);
				} else {
					SnesHelpers.WriteW(wr + 1, 0); // placeholder
					Error(SnesHelpers.BranchDistanceError(distance, 2));
				}
			}

			TrackAndStep3();
		}
	}


	/// <summary>
	/// For absolute jumps and calls
	/// </summary>
	private void WriteOpJ(byte opcode, IExpressionReturn val) {
		if (TryAdvanceProgramCounter(3)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			if (val.NeedsRequest) {
				if (MissingTokenSeverity >= MissingTokenSeverity.AbsolutePointers) {
					JumpRequests.Add(new AbsolutePointerRequest(val, Offset + 1, Provenance, WriteTime, CurrentSourceLine));
				} else {
					CreateSimpleRequest(val, Offset + 1, 2);
				}
			} else {
				int vval = val.ValueInt32;

				if (MissingTokenSeverity >= MissingTokenSeverity.AbsolutePointers) {
					WarningIfWeirdLocalJump(vval, Provenance);
				}
				SnesHelpers.WriteW(wr + 1, val.ValueInt32);
			}
			TrackAndStep3();
		}
	}

	private void WriteOpW(byte opcode, int val) {
		if (TryAdvanceProgramCounter(3)) {
			byte* wr = PcPointer;
			wr[0] = opcode;
			SnesHelpers.WriteW(wr + 1, val);

			TrackAndStep3();
		}
	}

	private void WriteOpW(byte opcode, IExpressionReturn val) {
		if (TryAdvanceProgramCounter(3)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			if (val.NeedsRequest) {
				CreateSimpleRequest(val, Offset + 1, 2);
			} else {
				SnesHelpers.WriteW(wr + 1, val.ValueInt32);
			}

			TrackAndStep3();
		}
	}

	private void TrackAndStep3() {
		uint* mod = TimeBuffer + Offset;
		uint wt = WriteTime++;
		mod[0] = wt;
		mod[1] = wt;
		mod[2] = wt;
		AdvanceProgramCounter(3);
	}

	private void WriteOpL(byte opcode, IExpressionReturn val) {
		if (TryAdvanceProgramCounter(4)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			if (val.NeedsRequest) {
				CreateSimpleRequest(val, Offset + 1, 3);
			} else {
				SnesHelpers.WriteL(wr + 1, val.ValueInt32);
			}

			TrackAndStep4();
		}
	}

	private void WriteOpL(byte opcode, int val) {
		if (TryAdvanceProgramCounter(4)) {
			byte* wr = PcPointer;
			wr[0] = opcode;

			SnesHelpers.WriteL(wr + 1, val);

			TrackAndStep4();
		}
	}

	private void TrackAndStep4() {
		uint* mod = TimeBuffer + Offset;
		uint wt = WriteTime++;
		mod[0] = wt;
		mod[1] = wt;
		mod[2] = wt;
		mod[3] = wt;
		AdvanceProgramCounter(4);
	}

	#endregion

	// FF_8* covers anything completely out of bounds
	private const int BankGuardMask_Off = unchecked((int) 0xFF_80_0000);
	private const int BankGuardMask_Half = unchecked((int) 0xFF_FF_8000);
	private const int BankGuardMask_Full = unchecked((int) 0xFF_FF_0000);


	private int BankGuardMask = BankGuardMask_Half;

	internal bool TryAdvanceProgramCounter(int step) {
		if (!TestOffsetBounds(Offset + step)) {
			if (!ShutUpAboutAddresses) {
				Error("The program counter has escaped the valid ROM region.");
				ShutUpAboutAddresses = true;
			}
			return false;
		}

		int pctest = PC + step - 1;
		pctest ^= PC;
		pctest &= BankGuardMask;

		if (pctest is not 0) {
			if (!ShutUpAboutAddresses) {
				Error("A protected bank boundary was crossed during assembly.");
				ShutUpAboutAddresses = true;
			}

			AdvanceProgramCounter(step); // advance anyways
			return false;
		}

		return true;
	}

	#region Address move tests

	private List<Dock> Segments = [];
	private Dock? currentSegment = null;

	private bool ShutUpAboutSegments = false;

	private void TryMovePC(IExpressionReturn oper) {
		int addr;
		int offset;

		if (oper is ExpressionSymbol el && el.Item is Dock seg && el.Context is SymbolContext.RomAddress) {
			addr = seg.Address;
			offset = seg.BinaryOffset;
			currentSegment = seg;
		} else {
			addr = oper.ValueInt32;
			offset = AddressToOffset(addr);
			currentSegment = null;
		}

		if (TestOffsetBounds(offset)) {
			if (AddressIsRom(addr)) {
				// TODO make a single MovePc(int) and use that for all explicit moves
				PC = addr;
				Offset = offset;
				PcPointer = RomBuffer + offset;

				ShutUpAboutAddresses = false;
				ShutUpAboutSegments = false;

				TestProtectedSegments(offset);
			} else {
				Error("Not a ROM address.");
			}
		}
	}

	private void AdvanceProgramCounter(int step) {
		PcPointer += step;
		Site += step;
		Offset += step;

		StepPC(step);

		if (Segments.Count > 0) {
			StepCurrentSegment(step);
			TestProtectedSegments(Offset - 1);
		}
	}

	private void StepCurrentSegment(int step) {
		Dock? curSeg = currentSegment;

		if (curSeg is not null) {
			curSeg.Advance(step, OffsetToAddress);

			if (!ShutUpAboutSegments && curSeg.State is DockState.Overflowed) {
				Error($"Assembly of segment \"{curSeg}\" has escaped its protected region.");
				ShutUpAboutSegments = true;
			}
		}
	}

	private void TestProtectedSegments(int offset) {
		if (!ShutUpAboutSegments) {
			Dock? curSeg = currentSegment;

			foreach (Dock ptest in Segments) {
				if (ptest != curSeg) {
					if (ptest.OffsetIsInRegion(offset)) {
						Error($"Assembly has entered protected region \"{curSeg}\"");
						ShutUpAboutSegments = true;
						break;
					}
				}
			}
		}
	}

	private bool TryToResize(int newSize) {
		switch (OverflowAction) {
			case RomOverflowAction.Error:
				if (!ShutUpAboutAddresses) {
					Error("Cannot assemble outside the ROM's binary range.");
					ShutUpAboutAddresses = true;
				}
				return false;

			case RomOverflowAction.Grow:
			case RomOverflowAction.Expand:
				if (ResizeRom(newSize)) {
					if (OverflowAction is RomOverflowAction.Expand) {
						CurrentRomSize = AllocatedRomLength;
					}

					return true;
				}

				if (!ShutUpAboutAddresses) {
					Error("The ROM cannot be enlarged any further.");
					ShutUpAboutAddresses = true;
				}
				
				return false;
		}

		return true;
	}

	private bool ResizeRom(int desiredSize) {
		if (desiredSize < 1 || desiredSize > MaxRomSize) {
			return false;
		}

		int sizeChange = desiredSize.CompareTo(CurrentRomSize);

		if (sizeChange is 0) {
			return true;
		}

		int newAllocSize = SnesHelpers.GetAllocationSize(desiredSize);
		int oldAlloc = AllocatedRomLength;

		if (newAllocSize != oldAlloc) {
			ReallocRomBuffer(newAllocSize);

			// clear the newly allocated memory
			if (newAllocSize > oldAlloc) {
				new Span<byte>(RomBuffer + oldAlloc, newAllocSize - oldAlloc).Clear();
			}
		}

		CurrentRomSize = desiredSize;
		return true;
	}

	private void ReallocRomBuffer(int size) {
		RomBuffer = NativeMemory.ReAllocNice(RomBuffer, size + RomBufferPadding);
		TimeBuffer = NativeMemory.ReAllocNice(TimeBuffer, size + RomBufferPadding);
		AllocatedRomLength = size;

		PcPointer = RomBuffer + Offset;
	}

	/// <summary>
	/// Clean up the unmanaged buffers.
	/// </summary>
	private void DeallocRomBuffer() {
		if (RomBuffer != default) {
			NativeMemory.AlignedFree(RomBuffer);
			RomBuffer = default;
		}

		if (TimeBuffer != default) {
			NativeMemory.AlignedFree(TimeBuffer);
			TimeBuffer = default;
		}

		PcPointer = default;
	}

	private void ResetRomBuffer() {
		CurrentRomSize = InitialRomSize;
		ReallocRomBuffer(SnesHelpers.GetAllocationSize(InitialRomSize));
		NativeMemory.Clear(TimeBuffer, (nuint) (TimeSize * AllocatedRomLength));

		// the baserom should count as 0
		WriteTime = 1;

		if (BaseRom is null) {
			NativeMemory.Clear(RomBuffer, (nuint) AllocatedRomLength);
		} else {
			int baseromlen = BaseRom.Length;
			int remaining  = AllocatedRomLength - baseromlen;

			if (remaining < 0) {
				remaining = 0;
				baseromlen = AllocatedRomLength;
			}

			fixed (byte* filler = BaseRom) {
				Buffer.MemoryCopy(filler, RomBuffer, baseromlen, baseromlen);

			}

			if (remaining > 0) {
				new Span<byte>(RomBuffer + baseromlen, remaining).Clear();
			}
		}
	}

	#endregion
}
