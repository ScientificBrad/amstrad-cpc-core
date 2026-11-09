# Known Issues & Limitations

**Project:** Amstrad CPC Computer Family Reconstruction  
**Researcher:** ScientificBrad

---

1. **Plus-Series & GX4000 ASIC Features:**
   - Reserved for Phase 11 & Phase 12 milestone after Classic CPC (464, 664, 6128) achieves verified stability.
2. **Extended DSK Weak Bit Emulation:**
   - Speedlock copy protection schemes that rely on weak/fuzzy bit transitions during raw MFM read are marked as known limitations until low-level flux/FDC state machine is expanded.
3. **Audio Buffer Latency on High-DPI Windows Displays:**
   - Monitored during Windows Forms audio driver integration; using direct circular ring buffer to ensure < 20 ms sound latency.
