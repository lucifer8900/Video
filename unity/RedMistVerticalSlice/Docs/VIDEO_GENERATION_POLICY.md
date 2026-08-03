# Video generation policy

## Hard rule

Do not call the Gemini API, the Gemini Enterprise Agent Platform video API, the Vertex AI Veo API, or any other billable video-generation endpoint unless the project owner explicitly requests a video-generation run in the current conversation.

Creating prompts, shot lists, first-frame images, Unity timelines, local placeholder animations and import manifests is allowed. These actions must not submit an API generation request.

## Preferred route after explicit authorization

1. Prefer **Veo on Gemini Enterprise Agent Platform** for this project when the owner's promotional quota or credits apply there.
2. Confirm the selected Google Cloud project is `video-game-ai-dev`.
3. Confirm the credential is accepted by the Agent Platform video-generation API and the target model has available fixed quota.
4. Show the owner the exact model, number and duration of clips, resolution, output location, and whether the operation can create charges.
5. Submit only after that explicit approval. A connectivity test that generates a clip also counts as a generation request and is forbidden without approval.
6. If Agent Platform authentication, quota, credit attribution or cost cannot be verified, do not fall back silently to the Gemini API or Vertex AI billing. Export the prompts to `Docs/VEO_AGENT_PLATFORM_PROMPTS.md` for manual generation in Media Studio.

## Import rule

Generated video files are optional presentation assets. The playable story must remain functional without them, and no story node may block on a remote API call.
