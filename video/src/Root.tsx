import React from "react";
import { Composition } from "remotion";
import { Promo, totalFrames } from "./Promo";

export const RemotionRoot: React.FC = () => (
  <Composition id="Promo" component={Promo} durationInFrames={totalFrames} fps={30} width={1280} height={720} />
);
