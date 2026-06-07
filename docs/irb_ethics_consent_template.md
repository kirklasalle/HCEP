# IRB Informed Consent & Biometric Data Policy Template
## Project: Human Communication Eye Protocol (HCEP)

**Study Title:** Empirical Validation of the Human Communication Eye Protocol (HCEP)  
**Principal Investigator:** Kirk LaSalle  
**Institution/Location:** HCEP Labs / Partner Institutions  

---

### 1. Introduction & Purpose
You are invited to participate in a research study validating the Human Communication Eye Protocol (HCEP). HCEP is an open-source framework designed to model and improve human-computer interaction, robotics, education, and mental health support by detecting non-verbal communication cues (eye contact, head pose, facial expressions, and speech).

The goal of this study is to measure the spatial accuracy of HCEP's eye-tracking and evaluate how reliably it classifies cognitive-emotional states.

---

### 2. Description of Procedures
If you agree to participate, you will be asked to:
1. Sit in front of a computer monitor equipped with a standard camera/sensor.
2. Complete a brief 9-point eye calibration by focusing on targets on the screen.
3. Perform short cognitive and emotional tasks (e.g., solving simple analytical puzzles, watching short emotional film clips, speaking to an interactive on-screen avatar).
4. Participate in a session lasting approximately 30 minutes in total.

---

### 3. Biometric & Personal Data Collection
This study involves the capture and processing of biometric information:
- **Video & Spatial Data:** Facial coordinate points, head position/rotation, eye socket centers, and facial expression muscle activations (Action Units).
- **Audio Data:** Voice recordings for speech-to-text transcription.
- **Biometric Templates:** Numerical mathematical representations (embeddings) of your face to evaluate the face recognition system.

> [!IMPORTANT]
> **Data Minimization:** Raw video frames and audio clips are processed in real-time in local memory. They are immediately discarded after feature extraction unless you provide separate explicit consent to retain raw media for research publication.

---

### 4. Data Protection, Security, and Storage
We implement industry-standard safeguards to protect your data:
- **Cryptographic Encryption:** All stored biometric templates and interaction logs are encrypted at rest using the Windows Data Protection API (DPAPI), binding access exclusively to the local system user account.
- **Local Isolation:** No biometric data, video streams, or audio recordings are uploaded to cloud servers. All processing and storage occur entirely on the local device.
- **Anonymization:** Your data is assigned a randomized subject ID. No direct identifiers (name, email) are stored with the biometric logs.

---

### 5. Data Retention & Erasure Policy (Right to be Forgotten)
In compliance with GDPR, BIPA, and standard institutional IRB ethics guidelines:
- **Automatic Expiration (TTL):** All biometric templates and session logs automatically expire and are purged from the database after 30 days of inactivity.
- **Explicit Erasure:** You have the right to request immediate deletion of all data associated with your session. The system features a dedicated erasure API to scrub your biometric embeddings from the database.

---

### 6. Voluntary Participation & Right to Withdraw
Your participation is entirely voluntary. You may refuse to participate or withdraw your consent at any time during the study without any penalty or loss of benefits. If you withdraw, all biometric records generated during your session will be immediately erased.

---

### 7. Consent and Signatures
By signing below, you acknowledge that you have read and understood the information above, have had your questions answered, and voluntarily consent to participate in this study under the stated biometric data terms.

```
Participant Name (Printed): _____________________________________________

Participant Signature:      ___________________________   Date: _________

Researcher Signature:       ___________________________   Date: _________
```
